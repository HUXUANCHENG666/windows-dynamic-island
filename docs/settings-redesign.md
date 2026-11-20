# t56：桌面设置后台重做 + 9 DIP 反射边

这是实际 `SettingsWindow.xaml` / BAML 与事件代码，不是网页或设计稿。未生成仿制 WPF 截图。

## 结构与视觉

- 固定154 DIP左侧分类导航，默认「外观与停靠」；右侧固定分类标题 + 独立可滚动的单页内容，页面切换不重建输入字段。
- 五个面板：外观与停靠、翻译、通知、隐身与应用、快捷工具。去掉整屏堆卡片，统一暗色表面、留白、字号、控件高度和薄荷色选中/焦点反馈。
- 可操作项从只支持鼠标的Border改成真正的Button / RadioButton / ToggleButton / Slider / TextBox / PasswordBox；标准Tab、方向键、空格与滑块键盘语义，含清晰焦点、标签、自动化名称。关闭和未保存提示也可键盘操作。
- 默认860×660 DIP，可调整；当前显示器的物理工作区按DPI换算后限窗，最小值在小屏下降低。右侧长内容/接口字段可滚动；导航不随内容滚动。100/125/150%等效逻辑工作区纳入CI无头布局检查，**不等于真实WM_DPICHANGED、多屏拖动或键盘实测**。

## 功能清单（全部保留）

| 分类 | 原有能力 / 入口 |
|---|---|
| 外观与停靠 | 85～140%大小、还原、近黑/玻璃皮肤；设置页新入口复用已有显示器选择与八向停靠/逐显示器位置记录，不改变拖动停靠机制 |
| 翻译 | 实时翻译开关、自动/中英/英中方向、默认及OpenAI兼容接口、地址/模型/密码框密钥 |
| 通知 | 原有系统通知启停与原有权限流程，仅主动点击启用；打开设置或分类不会请求权限 |
| 隐身与应用 | 已有手动隐身开关的新入口、隐身名单手输/Enter/选择exe/先前应用/运行进程候选/刷新/逐项移除、去重/拒绝自身及系统宿主 |
| 快捷工具 | 剪贴板翻译、上次翻译（无记录时禁用）、摄像头、实时翻译、秒表、25分钟专注、打开日志 |

没有新增后台权限、网络调用、模型、数据收集或联网测试按钮。近黑岛、托盘、置顶非激活、拖动/Q弹未改。设置持久化仍通过原有IslandWindow方法与SettingsStore，仅改动目标字段；其他设置（含计时）不重置。

## 修正初始化写回与API草稿语义

原先先挂TextChanged/PasswordChanged再填字段，`PersistApi`逐次保存整组三字段，初始化中就可能把尚未填充的模型/密钥覆盖为空。新版初始化/刷新有加载保护；其他偏好即时应用，**仅API一组显式保存**。保存前只复用本地HTTPS/loopback HTTP及模型/密钥非空校验，错误不回显用户输入。默认接口也保留已填自定义字段，便于切回。切页、激活、其他即时动作保留未保存草稿；撤销明确回到持久配置；关闭脏草稿有保存并关闭 / 放弃接口修改 / 继续编辑。无逐键写盘，无新增密钥日志；密钥仍沿用已有本地设置存储，未声称新增加密。

## 反射边

- 权威 `IslandOutline.RimWidthDip` 从6调到 **9**；实际 `min(9×DPI, shortSide×.20)` 物理像素。534×230、100%时9 DIP；180×40时8 DIP；85%紧凑高度34时6.8 DIP。不是回到短边26%的粗管。
- 外框1.5 DIP高光描边独立不变；内沿仍由同一外SDF等距内缩。CPU和HLSL共用该常量（GPU常量缓冲传入），5个局部双线性tap、共用RGB坐标、连续圆角法线、无色散、异步有界管线均保留。
- Linux实际Core CPU回退，534×230/r35/带流体，20次预热后180次：本次均值2.898ms、p95 4.877ms、测量循环0字节分配。只含预分配算法，不含捕获/任务/池/UI/GPU/呈现，**不是Windows FPS或60帧证明**。

## 可复现验证与边界

```
python3 build/check-settings-xaml.py
dotnet test tests/DynamicIsland.Core.Tests/DynamicIsland.Core.Tests.csproj -c Release
dotnet build src/DynamicIsland/DynamicIsland.csproj -c Release -p:EnableWindowsTargeting=true -warnaserror
# 下面只在Windows：
pwsh ./build/check-glass-shader.ps1
dotnet run --project tools/GlassGpuCheck -c Release
dotnet run --project tools/SettingsUiCheck -c Release
```

- Core261项；新增16项覆盖9DIP各尺寸/缩放/DPI限幅、窗口逻辑工作区与本地API验证/错误不回显输入；原有空间采样、灰阶不串色、几何与跨图层连续性回归继续执行。
- 静态XAML检查：53个窗口命名控件、25个事件处理器、35个资源引用、44项功能控件契约与ElementName目标；ControlTemplate分别按合法独立namescope检查。
- Windows CI继续fxc VS/PS4.0/5.0严格编译与12例真实HLSL的D3D11 WARP读回对照CPU（软件设备，不是硬件FPS）。
- `SettingsUiCheck`在STA线程加载**实际应用BAML和自定义模板**，使用纯内存控制器，不Show窗口、不启动岛/音频/摄像头/网络、不发权限、不写用户设置；检查五页布局/固定导航/横向溢出、初始化与切页不写配置、草稿保护、本地验证/保存/撤销、即时选项、停靠/显示器、隐身名单、全部七项动作、未保存关闭分支。运行结果见对应CI日志。
- 仍未实际显示Windows桌面窗口；字体观感、悬停/焦点/真实键盘、辅助技术、多屏DPI切换与裁切、通知授权及各工具真实服务联动，需要真实Windows桌面检查。无头布局通过不是视觉验收，不提供虚假运行截图。真实硬件GPU/端到端60fps仍未知。

正式main、v1.0.0 tag、Release不变，assembly仍1.0.0；仅推未合并测试分支。
