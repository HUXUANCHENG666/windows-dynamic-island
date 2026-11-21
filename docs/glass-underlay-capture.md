> t69 更新：在实际 CI 测得 BitBlt 捕获 p95≈31ms 后，Live 改为持续 DDA 优先，BitBlt/透明依次回退；静止也有清水透镜。下文是 t67 历史方案，当前实现与限制见 [清水透镜](glass-water-lens.md)。

# t67：设法取得岛正下方的实况像素

目标是让玻璃内部也能像边缘一样，对「此刻」正下方的真实桌面/窗口做算法扭曲，而不是在 BitBlt 挖洞后的空洞上补，也不是贴回历史截图。本轮只接通捕获与现有活体采样，没有做鱼眼改版，也没有换成系统 Acrylic / Mica。

## 调查结论（公开 API）

1. **不带 CAPTUREBLT 的 BitBlt。** 在 Windows 7 上曾能排除 `WS_EX_LAYERED` 窗口、露出底下内容。Windows 8 起 DWM 常年合成后，这条路径基本失效，拷到的是分层窗自己。不能当作现代默认方案，探针仍会跑一次作为对照。
2. **`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`（0x11）。** 这是正式公开 API，从 Windows 10 2004（build 19041）起把本窗口从捕获中拿掉，「其它地方完全不出现」，从而露出底下那一层。官方文档写明：在更早的系统上**设置同一标志不会失败，而会静默按 `WDA_MONITOR`（0x1）处理，捕获里是黑块**。因此必须先做版本门槛，绝不能在旧系统上设置。`WDA_MONITOR` 本身只会产生黑块，本程序不用。该标志被 Windows.Graphics.Capture 和 DXGI Desktop Duplication 尊重；社区与文档也有 BitBlt 在 2004+ 上同样省略该窗的报告，但必须靠像素检测确认，不能假定。
3. **Magnification API 排除本窗。** 公开，但缩放回调已弃用，还要额外放大窗口，DPI/多屏问题多。本轮不采用。
4. **Windows.UI.Composition `CreateBackdropBrush` / `CreateHostBackdropBrush`。** 更接近系统合成器在合成前取样底下内容的方式，但背景画刷通常只能显示、不能把像素读回给现有 5-tap / 位移场采样。要走这条路就得把折射整段搬进 composition 效果图，属于另一项架构工作，本轮不做，以免扩大范围，也避免把自定义玻璃换成 Acrylic/Mica。

没有使用未文档私有 API，没有 hook 其它进程，没有用 `Window.Opacity` 或短暂隐藏窗口来拷屏（会闪）。

## 选定方案

玻璃皮肤打开时，若系统 build ≥ 19041，则只对灵动岛顶层窗口设置 `WDA_EXCLUDEFROMCAPTURE`（置顶、`WS_EX_NOACTIVATE`、不进任务栏、自定义黑色托盘均不改；设置窗和托盘不加此标志）。关闭玻璃或退出时恢复 `WDA_NONE`。

捕获仍先走现有一次局部 BitBlt。若允许取正下方，先**不挖洞**，用 `GlassUnderlay.Classify` 看内部是实况、黑块，还是空的：

- **Live**：垫边有内容、内部也有有效像素，且不是「垫边有颜色、内部几乎纯黑」。纯黑壁纸（两边都黑）仍算 Live。保留内部像素，供现有位移场采样。
- **BlackHole / Missing**：再试公开的 DXGI Desktop Duplication（同一排除标志下复制桌面）。成功并再次判定为 Live 则采用。
- 仍不行则回退到原来的圆角挖洞，内部继续只扭曲垫边，真透明中心透出实况。检测与回退都有单元测试；某次 API 变黑块时不会一直用黑像素去填内部。

内部采样不再因为坐标落在岛内就拒绝：捕获里有有效 alpha 就取实况（透明度提高到约 0.92，避免和合成器真透明叠成双重影像）；挖洞回退时这些样本仍是 0。静止后强度为 0，中心仍不覆盖。位移场、9 DIP 边、多点采样、无色散、Q 弹与 1:1 跟手保持不变。

副作用：玻璃开启期间，本岛可能不会出现在 Win+Shift+S、游戏栏或其它使用同一套捕获 API 的录屏里。关玻璃即恢复。

## 验证边界

- Core 对版本门槛、Live / 黑块 / 空洞 / 纯黑壁纸、挖洞毒色仍不被取用、以及实况内部可被位移场采样，都有单元测试。WARP 增加未挖洞的内部对照。这些都不是硬件 60fps。
- `tools/GlassUnderlayCheck` 在 Windows CI 上叠两个已知颜色的窗口跑 BitBlt（有/无排除）和 Duplication。这只是 runner 上的合成叠层观察，**不是用户真实桌面的证实**，也**不能**从 Linux 开发箱证明某条 Win32 路径一定露出底下内容而不是黑块。
- 未改捕获之外的翻译可靠性、新源和设置。不声称实际 60fps。
