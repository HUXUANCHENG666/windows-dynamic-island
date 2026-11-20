# t57：连续字幕与停止可靠性

## 可从代码与回归确认的问题（不是声称复现用户电脑崩溃）

1. `SpeechChunk`本有UtteranceId，但旧Transcript事件丢弃它，只传文字/语言/final。识别又过滤空或无效终稿；因此上一行Open不关闭，下个15秒片段的partial进入`Accept`时直接替换上一整段。即使终稿有效，旧跨句`ShouldMerge`还会把无句号旧稿与下一段1～2词partial合并重新Open，后续partial再次整行替换。原`Accept`还无条件接受极短final/partial，较完整识别可能瞬间坍缩。新真实路径贯穿UtteranceId，空final也送达并收束；下一id强制新行，迟到已淘汰id不能重新出现。较完整稿≥24字符且新稿不足55%时拒绝整段坍缩，空稿只收束；普通替换和适度减字仍接受，不采用“只增不减”。这一阈值是保护启发式，并非语音准确性证明，可能保留较长但错误的旧识别。
2. 原每个约260ms partial都Cancel/Dispose正在请求的同caption翻译，再发新请求。延迟>260ms就可能长期没有译文、频繁消耗免费额度。新Core `LiveTranslationPump`一个进行中+最多40个最新稿合并，不因partial取消在途请求，返回后再处理最新稿；旧译文可先显示，但永不倒退覆盖最新原文。失败/空译文保留原文和已有译文。已译完的同文终稿无需重发，在途相同内容去重，限流/超时由实际HTTP层处理。
3. 旧字幕以220字符显示截断作为后续重译依据，丢掉尾部；新身份路径保留完整识别文本，在HTTP层按服务限制安全分块。旧40条上限本身是明确会话滚动策略，只淘汰最旧，和某行被新句替换不是一回事。识别最终音频队列原无上限，现最多8个片段，partial合并、final按身份去重；过载计数与中文状态提示说明跳过，不能承诺算力不足时每段音频都能识别。字幕原文已显示的不会因网络失败消失。
4. 原UI整表重建仅保存绝对scroll offset，前面某行译文变高或40条头部淘汰会改变正在看的内容。现保存可见caption ID和行内偏移；新行/更高译文不会抢回底部，已被40条策略淘汰的可见最旧行则回到最早幸存项。
5. 原Stop未等待RecognizeAsync，就Dispose两个WhisperProcessor和factory，且忙标志提前false可让新会话与旧decode重叠；原`LoopbackEndpoint`只Join(2000)后立即Dispose `_opened/_pump/_bufferReady`，STA仍在Open/Wait/Pump/Set时可能使用已释放句柄。均是可证明的不安全时序，但**没有用户真实崩溃日志/堆栈，不能断言就是该机器崩溃的唯一原因**。

## 资源修复

- `AsyncSessionOwner`单个worker序列化会话，最多一个可替换待开始项（不是每次Start追加Task链）；Stop立即失效epoch并取消，返回可等待Task。新Start在旧会话decode及finally资源清理完成后才运行。CTS cancel/dispose由单独lease保护；取消注册不在owner状态锁内执行，避免callback反向访问状态锁死锁。
- 每会话独立factory、两processor、队列、segmenter、capture；识别单worker顺序执行，传递Whisper ProcessAsync取消token。先停止/等待STA生产者，再Dispose processor，最后factory，不与decode竞争。切换/停止可迅速返回UI，清理在后台继续，不用UI同步Wait/Dispatcher.Invoke等待资源。
- 停止取消注册立即发capture停止信号；STA自己关闭COM对象，真正结束且Open调用返回后才释放wait handles。重复Dispose共享完成任务，不做2秒超时后强行释放。端点打开/读取/关闭仍同一STA；默认设备重开有epoch检查，关闭后不能重新打开。
- 识别/网络回调在投递前与实际执行时均检查会话epoch和dispatcher状态，旧会话不重新打开已关面板。关闭程序保持dispatcher至异步清理完成，等待实时HTTP、剪贴板请求、识别/capture和glass捕获/渲染任务后才Dispose客户端和Shutdown；关闭期间禁止Nudge/Pulse重新启动渲染。Glass异步关闭还回收已完成但UI未消费的捕获资源，既有采样/帧预算不变。
- 诊断只记异常类型、过载/丢弃数量，不包含字幕、响应、地址或密钥。未以catch静默忽略生命周期错误；预期取消不报错，其他故障显示状态并保留历史/类型日志。

## 验证

- Core **298项**（本轮增加37项），Linux完整运行；Windows CI也运行同一套managed测试。
- 实际SpeechSegmenter→RecognitionWorkQueue→CaptionChain喂入12分钟连续合成PCM帧（36000个20ms帧），模拟较慢decode、partial合并、48个15秒边界与交替空终稿，最后40条完整保留；这是算法模拟，识别文字是fake，不是12分钟真实Whisper识别。
- 实际链测试空/缺终稿、新utterance、短坏稿/正常减字纠错、晚译文不回退原文、失败/空结果、长文本、75段40条淘汰；真实pump测试1000个高频修订在一次慢请求中合并、不饥饿、切源只更新待译、2000字幕请求上限40、晚到UI回调/100次重复停止；真实owner测试300次并发开始-停止、阻塞decode时1000次重启仅保留最后一个开始请求、decode拒绝立即取消时等待再释放/再开始，以及取消callback跨线程查询owner/pump队列状态的锁顺序。
- provider测试用实际HTTP构造/解析层+fake handler，不联网，用假Key；验证五类源协议、DeepL新旧host、MyMemory UTF-8无损分块、200内部quota、429/456/503退避、请求中取消、错误不泄露body/key、profile持久化互不覆盖及自定义中英方向。
- Windows `SettingsUiCheck`继续加载实际BAML，自定义控件、5页/逻辑DPI与所有动作不回退；新增provider字段/密钥隔离/草稿与保存、实际LiveTranslateView的回看锚点/行高度变化/40条淘汰/空final保行。25个未Open的真实LoopbackEndpoint重复异步Dispose验证handles初始化清理，**没有打开真实音频设备或执行Whisper native decode**。
- 原shader四配置严格编译、WARP12例、玻璃/圆角/多点取色回归保留。WPF Release `EnableWindowsTargeting=true -warnaserror`要求干净重建0警告0错误。

仍未验证真实Windows麦克风/播放设备、蓝牙/换默认设备、Whisper原生GPU/CPU取消、实际native崩溃、DWM呈现或稳定60fps；需真实机器和不含隐私的崩溃堆栈进一步确认。若原生驱动/decode完全不响应取消，安全清理会等待，而非强制释放正被使用的句柄/模型；不能承诺任意原生挂死都能无延迟退出。未读取用户机器日志或访问用户电脑。免费源条件及官网链接见 [调研](translation-providers.md)。
