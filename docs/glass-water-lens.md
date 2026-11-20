# 清水透镜与低帧龄链路（t69）

## 先定位再改

检查 t67 `dbf4813` 的链路后，发现捕获频率已是 1/60 秒，不能只再调一个 FPS 常量：

- 捕获在工作线程完成后仍等 `DispatcherPriority.Background`，UI 回调才发布 pad/解锁捕获；新图最早下一次动画 tick 才进渲染。
- GPU 在每次 `TryPresent` 开头才检查前一次 staging，结果又等下一个 UI tick 写入位图；存在不必要的跨 tick 等待。
- 原 `frame-age` 从渲染开始计，不含底图采集时间；输出也没带原始几何，展开途中旧尺寸结果可能被 Image 拉伸。
- 原内部算法只在惯性场激活时覆盖；静止中心是真透明，当然没有静态水滴透镜。
- Linux 箱上的同机合成 CPU 基线（80 次预热后样本，纯计算、无捕获/UI）：320×140 Live p50/p95 5.532/7.189 ms；640×280 Live 22.087/23.929 ms，超 16.67 ms 预算。不是用户硬件 FPS。

## 本轮实现

- 保留排除捕获公开接口、19041 门槛、黑块检测、DDA 与挖洞回退。不加私有 API、不 hook、不短暂隐藏或调 Window.Opacity。
- `WaterLens`：有效 Live 底图才用。中心近似 1.08×，椭圆归一化穹顶的平方控制收缩，非全圆夸张鱼眼；距边缘三倍 band 到四分之一 band 使用五次 smootherstep 连续混到原边缘折射公式。外形仍圆角、边仍 9 DIP。RGB 共用 UV，5-tap 平滑。惯性位移在 Live 下只保留 18%，靠边收敛，避免阅读区域游动。无时钟振荡，静止仍有适度放大。
- 内部和边缘在同一次 CPU pass / HLSL pass 中、用同一捕获及几何，不再拼两张异步图。无 Live 时只保留旧的透明/垫边回退，绝不伪造底下内容。
- 捕获先启动，渲染工作任务直接 await 当次在飞捕获（不挡 UI），用最新完成 pad；捕获完成直接发布，不再绕 Background dispatcher。
- GPU 仍为硬件优先、3 staging 槽、DO_NOT_WAIT；提交后工作任务最多 3 次异步 1ms 延迟再非阻塞检查，没有 UI 等待/自旋，完成即发一个 Render 优先级 UI 提交通知，不强制等下个动画 tick。计时器可能被 OS 延迟，1ms 不是硬实时保证。只有一项渲染、一项捕获、一个完成槽；慢机器丢旧帧，不排队。
- 从采集起的时间戳、原尺寸/位置/缩放跟随各 staging 槽；UI 拒绝与当前几何误差超过 3 物理像素×DPI 或底图龄超过 100ms 的结果，过期已显示图也清掉；缺失区域是真透明，绝不把旧截图放大填上。Clear 使采集 epoch 失效。
- GDI 用延迟启动的专用线程（有界单槽 mailbox；按 [CreateDC 官方生命周期](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createdcw) 保持创建线程活着）复用 `CreateDC("DISPLAY")` 的显示 DC 与桶 DIB（失败重建、退出释放），不再每帧 GetDC/ReleaseDC；仅清请求矩形，防屏外裁剪残留旧像素。
- CPU 全内景 5-tap 比薄边贵，回退输出长边有界到 480；GPU 长边仍 1024。UV 与透镜公式一致，CPU 降分辨率是明确取舍，不影响真实窗口 1:1 跟手、Q 弹或展开弹簧。

## 诊断与证据范围

10 秒一条 `glass` 日志：capture/s、completed/s、ui-submit/s；capture、render-worker（含等当前捕获与有界 polling）、ui-write、gpu-submit-poll、gpu-poll-readback、capture-to-ui-age、ready-to-ui-age 的 p95；跳帧/替换/失败计数。日志没有屏幕内容。ui-submit 不是显示器实际 present FPS。

`GlassPerfCheck` 为 Linux/Windows 运行的预分配合成 CPU 计时，不设脆弱机器速度断言；`GlassGpuCheck` 用 WARP 对照静止/位移/缩放/展开收起、多 DPI 的 CPU 像素与 staging 几何；`GlassPipelineCheck` 在 CI runner 桌面跑实际捕获→渲染→回读→WPF WritePixels，带连续尺寸变化、Clear 和退出，并输出完整诊断。它不是用户 Windows、不是物理显示器 FPS。`GlassUnderlayCheck` 继续验证排除前后两个已知颜色窗口。

本地改后合成 CPU：320×140 Live p50/p95 5.423/7.316 ms；640×280 几何、480×210 输出 11.632/12.083 ms；预分配 Render 调用 0 bytes/frame。抖动会影响微基准，不能将这些数字解读为全链路 60fps。CI 的实际数字以对应 run 日志为准。

仍需用户硬件实测：真实桌面流畅度、驱动/多屏/保护内容、WPF/显示器实际呈现、快速拖动时的丢帧。目标 16.67ms，但没有声称在未测机器达到 60fps。超预算或捕获失败时可暂时退透明，而不是滞后拉伸截图。

CI 首轮扩展 WARP 覆盖发现 200% DPI、宽 420 DIP、缩放 1.08 时的真实缺陷：五 tap 在实际捕获边界附近越入较大的复用纹理桶，GPU 混进桶内空像素而 CPU 已 clamp 到内容边界（最大通道差 27）。已把 GPU 每个 tap 的 clamp 改为实际 SrcW/SrcH，保留该边界回归，不放宽 4/255 阈值。
