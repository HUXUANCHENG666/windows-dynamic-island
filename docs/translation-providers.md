# 免费翻译源核查（2026-11-20，Asia/Shanghai）

只读官方公开文档，不注册、不绑卡、不申请/收集任何Key，不使用网页逆向接口。下列“免费”不是无限免费；本程序不能判断Key背后的计费计划，不自动开通/升级/轮询多个源。原有选择保持到用户明确保存。

| 选项 | 官方免费性质、额度 | 账户/Key/卡 | 中英、地区与限制 | 接入/验证 |
|---|---|---|---|---|
| MyMemory（已有） | 匿名5000字符/日；有效联系邮箱可50000/日，本程序没有擅加邮箱；单次q最多500 UTF-8 bytes | 匿名无需注册/Key/卡 | en/zh-CN；公网可用性取决于网络与配额，连续字幕很快用完 | 保留默认；box一次公开`Hello`得到HTTP200/你好/quotaFinished=false |
| Azure Translator F0 | 标准翻译+自定义训练合计200万字符/月，免费层不失效；每订阅仅一个F0资源。不是30天$200试用额度 | 自行建Azure订阅/F0资源及Key；通常电话+信用/借记卡验证。免费账号30天后继续免费服务也需转PAYG订阅；务必保留资源F0，其他服务/层可能收费 | en/zh-Hans；适配全球端点，不适配世纪互联中国云Key。官方免费账号优惠不含主权Azure China；未验证中国大陆开户/网络 | 新适配官方REST，Key和资源region；只有fake HTTP协议/解析/退避验证，无真实Key调用 |
| DeepL Developer / 旧API Free | **当前新Developer共100万字符，耗尽不重置**；旧Free每月50万，官网称已不能新购买。不能把旧额度当新用户永久免费 | 新Developer官网称免卡，需要账户及Key；不注册、不验证用户资格 | 支持中英；默认api.deepl.com，旧Free `:fx` Key使用api-free.deepl.com；地区资格/可达性未实测，不从付费国家列表推断免费Developer在大陆能否开通 | 新适配官方POST；fake HTTP验证新/旧host及解析；未真实联网翻译，无免费计划保证 |
| LibreTranslate自建 | 开源软件免费，不是公有云免费额度；机器/运维/模型成本自理。自建请求/字符额度由管理员配置，默认无应用配额不等于无限算力 | 自建默认可无Key；受限实例需自己的Key；软件本身不要求卡 | 官方zh↔en模型；只装了哪些语言决定哪些可用，可本地离线。HTTP只允许loopback，其余HTTPS | 新适配 `/translate`、可选Key；保留子路径。官方托管站一次`Hello`匿名POST实测400要求Key，**不默认填入任何公共实例**。未部署真实本地模型，fake HTTP验证 |
| Google Cloud Translation NMT（调研，未接入） | 前50万字符/月，以$10月度credit抵扣Basic+Advanced合计，不结转；不是90天试用金，超额自动收费 | 需要项目、启用API、认证及billing account；一般付费结算资料，未走开户核实具体卡种/资格 | 官方支持中英；地区/大陆可用性未实测 | 官方定价/setup已查。因必须启用计费且超额风险，本轮不增加该适配，不冒充免Key接口 |

没有找到可以负责任默认启用的“三个永久、免密钥、公有云免费API”。新增三项中Azure是有条件长期月额度，DeepL新用户是一次性免费额度，Libre是开源自建；用户需自行准备相应条件，不能宣称装包后这三者都已可用。没有采用来源不明的公共镜像、网页抓取Google/Bing接口或隐式代理。

## 官方证据（均本轮打开）

- MyMemory usage/单次500 bytes：[额度](https://mymemory.translated.net/doc/usagelimits.php)、[技术协议](https://mymemory.translated.net/doc/spec.php)。WebFetch正文漏掉匿名行时另用WebSearch官方索引确认5000字符/日；并非猜测。
- Azure：[F0定价](https://azure.microsoft.com/en-us/pricing/details/translator/)、[F0不失效/资源限制](https://learn.microsoft.com/en-us/azure/ai-services/translator/how-to/create-translator-resource)、[免费账号/绑卡/30天/PAYG/主权云说明](https://azure.microsoft.com/en-us/free/free-account-faq/)、[REST](https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/v3/translate)、[中英支持](https://learn.microsoft.com/en-us/azure/ai-services/translator/language-support)、[速率](https://learn.microsoft.com/en-us/azure/ai-services/translator/service-limits)。F0小时窗口200万字符，应均匀使用；月额度仍200万，不是每小时额外赠送。
- DeepL：[新旧计划区别](https://support.deepl.com/hc/en-us/articles/360021200939-DeepL-API-plans)、[新Developer免卡与当前端点](https://www.deepl.com/en/developers)、[鉴权/旧Free后缀](https://developers.deepl.com/docs/getting-started/auth)、[语言](https://developers.deepl.com/docs/getting-started/supported-languages)。不同官网页面处于新旧计划过渡，保留两类Key端点，不把旧Free说明套用到新Developer。
- Libre：[安装/配额/自建](https://docs.libretranslate.com/guides/installation/)、[API/Key可选条件](https://docs.libretranslate.com/guides/api_usage/)、[中英模型](https://docs.libretranslate.com/guides/supported_languages/)、[托管站](https://libretranslate.com/)。托管匿名POST返回`Visit ... to get an API key`，没有继续注册。
- Google：[每月credit](https://cloud.google.com/translate/pricing)、[必须启用billing](https://cloud.google.com/translate/docs/setup)。

## 程序边界

- 设置明确选择MyMemory/OpenAI兼容/Libre/Azure/DeepL。API草稿按提供商分别保留；保存时校验当前源，保留其他已编辑配置。新profile不覆盖旧OpenAI URL/model/key；首次加载/切页/即时选项均不写回。仅已选提供商会用于新请求；切源更新待译任务，不取消历史，进行中的旧请求可先完成，绝不多源广播。
- 实时翻译一个进行中请求+最多40个最新caption待译稿，结果完成后间隔800ms；同文同源去重。高频partial不再取消进行中的请求；12秒单请求超时。HTTP429尊重Retry-After（1～300秒），456等quota冷却，5xx默认10秒；MyMemory HTTP200内部quotaFinished/429也冷却5分钟。冷却期间不访问网络、不自动切源。分块请求顺序执行，不并发。
- MyMemory按最多450个UTF-8 bytes分块并保留Unicode rune，避免旧400字符遇多字节超500 bytes；其他源大段按5000 bytes顺序分块。原文在本地字幕链保留完整，不因API分块/失败截掉。
- provider HTTP核心可注入fake handler，测试请求路径、header、JSON、语言映射、输出解析、取消、限流及原文完整；这不是持真实Key对云服务的实测。
- 不把响应body/请求URI/字幕/Key写日志，诊断只有类型/数量。HTTP错误不回显服务端body；不跟随HTTP重定向泄露鉴权。密钥仍是既有本机JSON配置方式，未新增加密，未写memory。只向用户明确选择并启用的服务发送文本；自建实例亦须用户信任。
