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
- CPU 全内景 5-tap 比薄边贵，最终回退输出长边有界到 384；GPU 长边仍 1024。UV 与透镜公式一致，CPU 降分辨率是明确取舍，不影响真实窗口 1:1 跟手、Q 弹或展开弹簧。

## 诊断与证据范围

10 秒一条 `glass` 日志：capture/s、completed/s、ui-submit/s；capture、render-worker（含等当前捕获与有界 polling）、ui-write、gpu-submit-poll、gpu-poll-readback、capture-to-ui-age、ready-to-ui-age 的 p95；跳帧/替换/失败计数。日志没有屏幕内容。ui-submit 不是显示器实际 present FPS。

`GlassPerfCheck` 为 Linux/Windows 运行的预分配合成 CPU 计时，不设脆弱机器速度断言；`GlassGpuCheck` 用 WARP 对照静止/位移/缩放/展开收起、多 DPI 的 CPU 像素与 staging 几何；`GlassPipelineCheck` 在 CI runner 桌面跑实际捕获→渲染→回读→WPF WritePixels，带连续尺寸变化、Clear 和退出，并输出完整诊断。它不是用户 Windows、不是物理显示器 FPS。`GlassUnderlayCheck` 继续验证排除前后两个已知颜色窗口。

本地改后合成 CPU：320×140 Live p50/p95 5.423/7.316 ms；640×280 几何、480×210 输出 11.632/12.083 ms；预分配 Render 调用 0 bytes/frame。抖动会影响微基准，不能将这些数字解读为全链路 60fps。CI 的实际数字以对应 run 日志为准。

仍需用户硬件实测：真实桌面流畅度、驱动/多屏/保护内容、WPF/显示器实际呈现、快速拖动时的丢帧。目标 16.67ms，但没有声称在未测机器达到 60fps。超预算或捕获失败时可暂时退透明，而不是滞后拉伸截图。

CI 首轮扩展 WARP 覆盖发现 200% DPI、宽 420 DIP、缩放 1.08 时的真实缺陷：五 tap 在实际捕获边界附近越入较大的复用纹理桶，GPU 混进桶内空像素而 CPU 已 clamp 到内容边界（最大通道差 27）。已把 GPU 每个 tap 的 clamp 改为实际 SrcW/SrcH，保留该边界回归，不放宽 4/255 阈值。

## CI 进一步定位：不能只改调度

60eb935 的实际 CI 全链路给出 capture p95=30.99ms、worker=33.43ms、capture-to-ui=35.54ms、UI submit=25.6/s（非显示 FPS）。这暴露 BitBlt 才是该 runner 的主瓶颈，因此没有停在“调度变快”的假设上：现优先持续复用 DDA output、零超时 AcquireNextFrame；无新桌面帧是“当前桌面未改变”，复用最近发布的整张 GPU 桌面而非截图拼贴。只在新 desktop update 到来时 CopyResource；每次只读回岛附近 ROI，输出仍用同一 UV/CPU/GPU 路径。DDA staging 改为增长桶，展开不再每像素重建。

为在 ReleaseFrame 后合法使用当前桌面，持有一张自己拥有的 GPU copy（最多 16,777,216 BGRA8 像素 / 64 MiB）；切输出或设备/访问丢失即销毁，绝不在错误后沿用。无新帧 timeout 才可复用。旋转屏、跨输出 ROI、超 64MiB 桌面、DDA 不支持/失败/黑块 → 复用 GDI DC 的 BitBlt → 非 Live 则挖洞透明。没有强行 GPU-only 改写 WPF 呈现。日志额外计数 capture-dda / capture-gdi；探针对“无更新仍正确”和“底层变色立即换当前桌面”做回归。此额外 GPU copy 的内存与旋转屏回退是明确限制，Windows CI 性能仍不能等同用户硬件。

### 再次测量后的节拍修正

188ffd7 在另一 CI runner 观察：DDA capture p95=1.04ms（386 次 DDA、1 次 GDI），但 worker p95=15.87ms、采集到 UI=16.27ms，UI submit=37.3/s。瓶颈已转到 Task.Delay(1) 的粗粒度唤醒/runner 的 DispatcherTimer 节拍，不能据此声称 60fps。最终用公开 [CreateWaitableTimerExW HIGH_RESOLUTION](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw) 的私有单次 1ms 等待代替 Task.Delay；只在后台最多等待两次，不自旋、不改变全系统 timer resolution；不支持时跳过等待、下一 tick 再取，句柄随 GPU 退出释放。UI 始终不等待。

同轮 Windows CPU 合成 480×210 p95=21.932ms，仍超预算，因此最终 CPU Live 长边进一步有界到 **384**（算法和 UV 不变，精细文字在 CPU 回退下可能比 GPU 软）；上文 480 的数字为调优过程，不是最终上限。最终 run 日志是交付指标。所有窗口/动画、翻译、设置和正式版号不变。

最终审查保留 WriteableBitmap 的可复用存储：拖动/过龄时只撤下旧图，不销毁位图；同尺寸新图仍 WritePixels 原对象。展开导致实际输出尺寸变化才重建，避免“拒绝旧几何”反而令普通拖动每帧创建 WPF 位图。

### 可复查的 CI 实测（同一测试方法，不是同一物理机器）

- BitBlt 优先 / run 44763793582：p95 capture 30.99ms，worker 33.43ms，capture-to-UI 35.54ms，UI submit 25.6/s。
- DDA + 粗 Task.Delay / run 44764756236：1.04 / 15.87 / 16.27ms，UI submit 37.3/s；386 次 DDA、1 次 GDI。
- DDA + 私有高精度等待 / run 44765563897：0.99 / 4.04 / 4.31ms，UI write 0.36ms，GPU submit/poll 0.22ms，readback probe/copy 0.02ms，ready-to-UI 0.42ms；UI submit 36.3/s，374 次 DDA、1 次 GDI。三个 capture-fail 是测试 Clear 使采集 epoch 作废的计数。39 个 WARP 对照最大通道差 3/255；无变化复用和底层变色更新均通过。
- 最后一项同 run Windows CPU：320×140 Live p50/p95 9.234/14.892ms；640×280 几何、384×168 输出 12.886/13.370ms，预分配调用 0 bytes/frame。

这些是共享 CI runner 的观测，不能严格归因百分比加速，也不能把 DispatcherTimer 驱动的 36.3 次 UI 提交说成真实显示 60fps。证据说明主要等待已被去掉、单帧链路 p95 进入 16.67ms 目标预算；真实 app 用 CompositionTarget.Rendering，用户硬件/显示器仍未测。Linux 仅合成算法图可检查空间连续，不是 Windows 录屏。
