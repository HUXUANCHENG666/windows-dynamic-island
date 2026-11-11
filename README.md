# 灵动岛

一块停在屏幕顶部正中的黑胶囊。有音乐时它显示封面和一条会动的波形；鼠标放上去，它用弹簧撑开，变成播放器。音量、插拔电源、倒计时结束，也会从这里冒出来。

它一直在最前面，但不抢焦点，不出现在任务栏，也不出现在 Alt+Tab 里。点岛外面的桌面，点击会穿过它。

需要 **Windows 10 版本 2004（19041）或 Windows 11**。程序自带运行库，不用另装 .NET。

## 预览

下面这些图是用**同一套超椭圆和弹簧**画出来的，字体是 Inter 和文泉驿。它们不是 Windows 上的实机截图。真正的窗口用 Segoe UI 和微软雅黑，阴影是 WPF 的投影。

![几种状态](docs/previews/states-light.png)

![深色背景](docs/previews/states-dark.png)

从紧凑到展开，采样的是弹簧自己的位置，不是手画的关键帧：

![展开过程](docs/previews/motion.png)

![圆角和弹簧曲线](docs/previews/corners.png)

动图在 `docs/previews/motion.gif`。

## 下载和运行

1. 拿到 `DynamicIsland-win-x64.zip`，解压。
2. 双击 `DynamicIsland.exe`。
3. 第一次会慢几秒。单文件程序要把运行库解到临时目录，之后就快了。

压缩包里有一份短说明 `readme.txt`。

### “Windows 已保护你的电脑”

这个 exe **没有代码签名**。SmartScreen 会说发行者未知。点 **更多信息**，再点 **仍要运行**。这是未签名程序的正常提示，不是程序坏了。

## 托盘、开机启动、退出

岛本身没有关闭按钮，也不接受键盘。所有开关都在托盘。

时钟旁边有一个小图标（黑胶囊，带一圈白边，深色和浅色任务栏都能看见）：

- **左键**：岛展开大约 5 秒。鼠标移上去会保持展开，移开后再收回。
- **右键**打开菜单：
  - **显示器**：默认主屏。点另一项就换到那块屏，并记住。
  - **开机时启动**：勾上之后，下次登录会自动打开。写的是当前用户的注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，名字 `DynamicIsland`，不需要管理员。取消勾选会删掉这一项。已经在运行的程序不会因此退出。
  - **打开秒表**、**开始 25 分钟专注**
  - **发送一条测试通知**
  - **打开日志**
  - **退出**

设置和日志在 `%AppData%\DynamicIsland\`：`settings.json` 和 `log.txt`。计时器会写进设置里，关掉再打开会接着走。如果倒计时是在程序没开的时候走完的，而且刚走完不久，岛会补一句“时间到”。

## 它会做什么

**音乐。** 读的是 Windows 的系统媒体控件（SMTC），也就是键盘媒体键、Spotify、浏览器里正在播的那一条。紧凑时左边是小封面，右边是波形。展开后有封面、歌名、歌手、进度（如果播放器提供了时长）和上一首 / 播放暂停 / 下一首。这些按钮会真的去控制那个会话。没有会话时，岛缩成一颗安静的胶囊，展开是时钟，可以在这里开秒表或 25 分钟专注。

正在播放的音乐会盖住计时器。音乐暂停时，已经打开的计时器会回到紧凑位。

**音量。** 系统音量或静音变化时，岛展开，显示百分比和一条音量条。按住音量键时它会停在最新的数值上，松手大约 1.7 秒后收回。第一次读到的当前音量不会弹通知。

**电池。** 有电池的电脑才会出现。插上电源、拔掉电源会提示。电量往下掉时，只在跌破 20%、10%、5%，以及每跨过 10% 时说一声，避免每掉 1% 都打扰你。台式机没有电池，这项是安静的。

**提示排队。** 你正看着展开的岛时，新的音量或电量提示会先等着。鼠标离开、岛收回之后，如果那条提示还新鲜，再把它放出来。

波形不是这首歌的频谱。系统媒体控件不提供音频采样。播放时，四根竖条按设计好的节奏摆动，高度再乘上默认播放设备的峰值；暂停时竖条收回去。

## 命令行

程序已经在跑的时候，再开一次会把参数交给它，不会开出第二个窗口。

```text
DynamicIsland.exe --toast 标题 正文
DynamicIsland.exe --timer stopwatch
DynamicIsland.exe --timer 25
```

不带参数再启动一次，等于让岛展开。

## 想改外观

颜色、尺寸、手感各有一个地方，改完跑测试。

| 想改什么 | 文件 |
| --- | --- |
| 颜色 | `src/DynamicIsland.Core/Island/Palette.cs` |
| 每种状态的宽、高、圆角 | `src/DynamicIsland.Core/Island/IslandMetrics.cs` |
| 弹簧有多快、过冲多大 | `src/DynamicIsland.Core/Motion/MotionTune.cs` |
| 内容什么时候淡入、模糊多少 | `src/DynamicIsland.Core/Motion/Easing.cs` |
| 圆角是正圆还是连续圆角（指数 n） | `IslandMetrics.cs` 里的 `CornerN`，算法在 `Geometry/Squircle.cs` |
| 界面怎么排 | `src/DynamicIsland/Views/` 里的 XAML |

现在的弹簧刚度 170、阻尼 18.3，过冲大约 4% 到 5%，半秒左右落定。紧凑态是正圆胶囊（n = 2，半径等于高度的一半）。展开的音乐卡用 n = 4.5 的连续圆角。变形过程中 n 和宽高走同一套弹簧，所以中途不会突然变成一个小圆角矩形。

`dotnet test` 会检查过冲幅度、胶囊面积、悬停延迟和电量提示的频率。手感改得太夸张，测试会先告诉你。

预览图可以用下面的命令重画（需要能跑 .NET 8 的环境，不需要 Windows）：

```bash
dotnet run --project tools/RenderMock/RenderMock.csproj -c Release -- docs/previews
```

## 自己编译

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```bash
dotnet test tests/DynamicIsland.Core.Tests/DynamicIsland.Core.Tests.csproj -c Release
./build/publish.sh
```

`publish.sh` 在 Linux、macOS、Windows 上都能打出 **win-x64、自带运行库、单个 exe**。Linux 上要能访问 NuGet，项目里已经打开了 `EnableWindowsTargeting`。

GitHub Actions（`.github/workflows/windows.yml`）会在 `windows-latest` 上打同一个 zip 并上传为构件。打 `v*` 标签时，会把它挂到对应的 GitHub Release 上。

`installer/DynamicIsland.iss` 是 Inno Setup 6 的脚本，**没有在这里编译过**。要装 Inno Setup 之后自己打开编译，并且把 `DynamicIsland.exe` 放在脚本旁边。

## 已知限制

- 只支持 64 位 Windows 10 2004 及以上。更老的 Windows 10 没有这份系统媒体控件的投影。
- 波形是指示动画加系统音量峰值，不是逐首歌的频谱。
- 进度依赖播放器有没有通过 SMTC 报告时长。有的程序不报，岛上就不画进度条，按钮仍然能用。
- 透明窗口在 WPF 里走软件绘制。岛所在的窗口大约 760×360 设备无关像素，不是全屏遮罩，这样动画才有机会保持顺滑。
- 岛外面的点击靠 `WM_NCHITTEST` 返回穿透。这套行为没有在真机上点过，如果发现某块区域点不下去，日志在 `%AppData%\DynamicIsland\log.txt`。
- 岛不拿键盘焦点，所以没有快捷键。
- 开机启动是登录时运行，不是服务，也不会在登录界面之前出现。
- 单文件第一次启动要解压本机库，杀毒软件可能因此多看一眼。
- 未签名，SmartScreen 会拦截一次。
- 计时器存在本机设置里。卸载或删掉 exe 不会自动清 `%AppData%\DynamicIsland`。

## 这台机器上验证过什么

开发和打包是在 Linux 上完成的，**没有 Windows 桌面，WPF 窗口没有跑起来**。

验证过的：

- 弹簧、超椭圆、悬停/通知状态机、电量与音量的判断、计时器、时间格式，共 46 个测试，在 Linux 上通过。
- 工程能交叉编译。打出来的 `DynamicIsland.exe` 是 PE32+ GUI、x86-64，里面带有 `coreclr` 和 `PresentationNative`，约 69 MB，不需要目标机器安装 .NET。
- 上面的预览图是这套几何和弹簧在 Linux 上渲染的。

没有在真机上验证的：

- 窗口是否真的不抢焦点、不进任务栏和 Alt+Tab。
- 弹簧和模糊在 WPF 透明窗口里的实际帧率。
- SMTC 读歌、封面、上一首/下一首/播放。
- 音量回调、电池插拔、托盘菜单、开机启动注册表、多屏和 DPI。
- Inno Setup 安装包。这个仓库的托管方也跑不了 GitHub Actions 的 Windows 托管机，工作流文件在仓库里，还没有一次真实的 CI 结果。
