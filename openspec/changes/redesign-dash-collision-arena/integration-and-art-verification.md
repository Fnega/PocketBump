# Dash 碰撞竞技场：实施与交付验证

## 当前交付摘要（2026-10-03）

- 交付目录：`D:/uniry Assets/射球大战/PocketBump0.13/`，启动 `PhysicscollGame.exe`；必须复制完整目录，不能只复制 exe。旧版本及失败候选 0.10/0.11/0.12 均保留，没有覆盖。
- 正式 MainMenu/Arena 已接入获批的圆润机器人和清新场地；碰撞射门、0.3秒锁方向Dash/结束后3秒冷却、Buff、柱晕、可碎/安全修复墙、掉落不计分/3秒安全复活及本地分屏/LAN单视口均已实现。详细数值以批准的生产 profile 为准。
- 本次长测发现的快端推进误报输入洞及校验饥饿已修正：保持回滚12 Tick/输入延迟2 Tick，安全领先10 Tick时先等待，继续收发，有效连续输入恢复；独立5秒无输入进展后冻结显示原因，不自动退出菜单。
- 最终全量Unity回归：`Tools/DashArenaIntegrationResults/20261003-input-pacing/editmode-3.xml` **551/551**、0 skipped、295.528秒；同目录 `playmode-3.xml` **85/85**、0 skipped、27.040秒。EditMode包含26项真实双socket会话案例；诊断/机制定向测试已包含在最终551项中，不重复累计。最新作者编辑持久化报告 `Tools/DashArenaPresentationResults/20261003-180433-66b3f8e5-authored-persistence/` 证明保存、重载、真实进出Play、增量修复仍保留模型/材质/手摆内容，正式两场景和Build Settings哈希未改。
- 新包完整本机双进程验收 **38/38通过**，索引 `Tools/DashArenaAcceptanceResults/20261003-172334-276-8dc5a3/acceptance-index.json`。76份两端最终报告与76份日志另经只读独立复核。包含普通移动、握手丢包、版本拒绝、两端停步、自然完整局、主客分别连续三次重赛/离开重连、14故障及16机制案例；负例通过表示按预期拒绝/暂停，不表示负例继续比赛。
- 实际Player本地分屏三尺寸及窗口/全屏往返已有证据；用户随后明确允许显示测试窗口，LAN三尺寸六份截图、CSV及窗口身份记录也全部通过。旧隐藏窗口黑图仍保留并排除，见下方最终可见复测小节。
- `ThirdPartyNotices/` 包含字体及现有Kenney资源的11份来源/许可文件，逐份原字节SHA256核验。游戏运行DLL最终哈希 `0D8C0C4815A34963E70D3C99C2D0A602126153EEFBDB28CB2BFD31FD9685DFD5`。
- **两台实体电脑的LAN/Wi-Fi验收未执行**。用户已确认与朋友使用同一完整新版本测试并提供两端记录；本机自动化不能替代该步骤。未承诺所有硬件、网络或复杂特效组合都没有问题，也未自动同步规范、归档或提交Git。
- OpenSpec实施清单 **52/52**；schema为`spec-driven`。最终校验器复测171+154+422=747项报告算法检查、46项截图/CSV检查、61项许可复制检查通过；这些合成检查不计入Unity用例或实际Player场次。原隐藏图的只读审计仍接受8份并拒绝1份黑图，不擦除历史失败。

## 分段历史记录（从2026-10-02开始）

以下按时间保留实施过程；“尚未完成”“等待确认”与阶段任务数描述其当时状态，不应覆盖上面的当前摘要。失败原始报告不改写。

## 已批准的范围

- 用户“可以就这样”接受灰盒参数和布局，已冻结独立生产 profile；原灰盒仍保持临时身份。
- 用户批准原创可编辑圆润机器人、沿用已许可资源、首版静音的独立 Unity 小样。不得据此覆盖正式美术。
- 用户另行批准下载 Noto Sans CJK SC Regular。原字体/许可保留于 `.art-source/fonts/noto-cjk/Sans2.004/`，Unity 副本于 `Assets/PocketBumpDashArt/Fonts/`，两者字节相同。来源与完整哈希见 `resource-candidates.md`。

## 新联机基础的边界

新增 v7 codec、完整规则哈希/显式种子握手、回滚会话、生命周期、真实 UDP 会话封装、确认事件账本及 Dash 输入绑定/锁存。原正式 v6 运行路径尚未切换，不以这些独立模块完成代替正式 UI、比赛驾驶器和发行验收。

代码审查修正了最终握手确认后可能产生回应回环的问题；增加第七个握手报文丢失覆盖。网络采样以 Pack/Unpack 后的实际线协议输入为准，避免诊断或未来模拟输入的任意浮点方向在两端不一致。事件通知逐订阅者隔离异常，不把表现错误归类为网络断线，也不重投已确认事件。

已知仍未完成：原 launcher 的开发故障注入只解码 v6，不能用其通过记录宣称覆盖新 v7；自然零分完整局、正式双进程、多分辨率 HUD、两台电脑 LAN 与发行版仍待后续实施。机制 fixture 和本机 loopback 不等于自然完整比赛或真实 Wi-Fi 验收。

## 测试记录

使用 Unity `2022.3.62f3c1`，独立隐藏 batch 进程；执行前用户已关闭编辑器。

| 结果文件（项目根目录下） | 结果及限制 |
| --- | --- |
| `Tools/DashArenaIntegrationResults/20261002-2002/editmode-1.xml` | 434/434 通过；该轮有一个新 sampler 测试文件因错误 meta GUID 未被发现，不算覆盖该文件 |
| `Tools/DashArenaIntegrationResults/20261002-2002/editmode-2.xml` | GUID 修正后 459/459 通过，0 skipped；含 12 个真实 loopback 会话测试与 sampler 测试。后续表现回调隔离和美术补充需最终复测 |
| `Tools/DashArenaIntegrationResults/20261002-2002/playmode-1.xml` | 41/41 通过，0 skipped，包含 16 个新美术样板案例及原有适用回归；近距离传送/复活不插值、开球禁 Dash 文案、撞柱挤压回弹、基材质保留及有界视觉回收均已验证 |
| `Tools/DashArenaIntegrationResults/20261002-2002/editmode-3.xml` | 最终 462/462 通过，0 skipped；含全部 15 个真实 loopback 会话测试，逐订阅者异常隔离以及机制 fixture 下同 socket 连续三次重赛通过 |

最后一项连续重赛测试使用明确标注的双方同初态、终局前机制 fixture，不是自然零分完整局驾驶器；实际 Step 跨门、比分展示、Finish/ACK、八秒健康结果等待、主/客分别单边意愿等待、双边同意及新 seed 初始化均经真实 socket 串联。验证四个 epoch 只各初始化一次、保留历史 finish 元组/种子/配置身份、新世界 Buff/墙/PRNG/接触及输入事件历史归零。测试模拟时钟推动等待，并非声称录制了每段八秒真实时间的双进程对局。

独立生产内容哈希：`7012a8bc484f3d522ace17ccc550c9572659c5d0d2f96fa122a83fa27831abb8`。
布局哈希：`13a8a46bb386b63ff20e427546d184a5eb932274c753ea8d6397e69c9dcaa500`。

## 样板与正式场景隔离

样板目标为 `Assets/Scenes/DashArenaArtSample.unity`，资源目标为 `Assets/PocketBumpDashArt/`。显式创建工具拒绝覆盖已存在的具体输出资产；默认加载/运行不重建场景、不改共享基材质。地图、机器人和相机仍只读取一个模拟世界；视觉碎片不加入权威碰撞。

捕获有两类，须分别标明：摆拍展示台只验证模型/材质/效果造型；真实模拟双视口图验证运行世界，但不是完整局证据。`Camera.Render` 不包含 IMGUI，不能将 world-only 图当作中文 HUD 或弹窗截图。实际样板 G3 尚需用户确认，概念图不代替实机验收。

生成前正式文件 SHA256：

- `Assets/Scenes/MainMenu.unity`：`F887E8F402E86843E6EDF1761B2158836D332F74AD4956852C34D4E8FD3260F0`
- `Assets/Scenes/Arena.unity`：`6DFAA836012FB4B830CC43AF29D80B12FCCEA57B3FD3F9B44A186CD566486A3C`
- `ProjectSettings/EditorBuildSettings.asset`：`905DF335CC8817706849000764C8FC2E4769CD71AF236190CF8CBA8C4594AFC9`

2026-10-02 实际生成已完成：8 个 Prefab、22 个材质、3 个原创网格及独立保存的样板场景。初次真实 Unity 捕获在 `Tools/DashArenaArtSampleResults/20261002-first-sample/`；`robots-closeup-staged.png`/`art-board-staged.png` 是摆拍展示台，`live-split-world-only.png` 是单模拟运行 240 Tick 的双视口，均非概念图。生成后三个正式文件 SHA256 与上表完全相同。

字体预检：`Tools/DashArenaArtSampleResults/font-validation-20261002-202302-096.txt`。实际 Unity 导入的内嵌 Noto Font 对 151 个字符，在 12/13/17/20 字号均通过 HasCharacter/GetCharacterInfo（缺字 0）；没有因此声称已经验证发行包或整个 HUD 排版。修复后没有新增中文字符。

编辑器查看入口：直接打开 `Assets/Scenes/DashArenaArtSample.unity`，或菜单 `Tools/Pocket Bump/Art Sample/Open Independent Sample`；基础几何和材质在编辑模式即可见。Play 后 P1 使用 WASD + 已保存 Dash 键（默认 K），P2 方向键 + Dash（默认小键盘 2）。实际运行含 HUD 捕获菜单为 `Tools/Pocket Bump/Art Sample/Capture Actual Running Sample With HUD`。修改颜色使用 `Assets/PocketBumpDashArt/Materials/`；替换/增加机器人细节使用 `Prefabs/Robot_Cyan.prefab` 与 `Robot_Orange.prefab` 的 `Motion` 子层级，保留控制器绑定的节点。

最终修复后从已保存场景再次捕获于 `Tools/DashArenaArtSampleResults/20261002-reviewed-sample/`，进程正常退出 0，未重建或覆盖任何模型/材质/场景资产；再次核对正式文件哈希仍完全相同。截图展示第一版清新、原创机器人造型，并未宣称精修动画、完整 HUD 排版或发行版效果已验收。

当前 OpenSpec 进度为 22/48；本段新完成 6.5、9.1、9.2、9.3，其余新增代码中的独立联机/输入能力留作正式运行路径整合前的已测基础，不提前勾选第 7/8/11 节。按 G3 门槛等待实际样板确认，再推进正式替换。未打包新版本、未覆盖已发布游戏、未同步或归档其他变更，不能把样板功能通过当作正式场景接入完成。

## 后续确认与正式接入（2026-10-02）

上面的 22/48 与 G3 等待描述为样板交付时的历史状态。用户随后回复“符合预期”，批准真实 Unity 样板的圆润机器人和米白/薄荷配色，9.4 已完成，进度变为 23/48；正式 HUD、动态反馈、多窗口分辨率与发行性能仍须分别验证。

用户另明确同意最后与朋友使用同一完整新版本执行双电脑 LAN 验收，交回两端报告。这只确定了协作安排，当前双电脑测试未执行。本机 socket 或双进程结果不得替代真实网络结论。

正在接入独立 DashMatchDirector、纯状态表现、TMP HUD、双 Dash 菜单与显式增量场景工具。首次迁移保留旧场景字节备份及停用的旧层级；不全量重建、不自动重涂已有主题，也不修改历史发布目录。正式场景与新驾驶器尚未验证时，不得沿用旧版诊断 PASS 宣称新版已通过。

## 正式场景接入与后续验证（2026-10-03）

上一节是开始迁移时的记录。现在正式 `MainMenu.unity` 和 `Arena.unity` 已保存新版菜单、配色、模型、Dash director、双视口和 TMP HUD；编辑器未运行时即可查看主体美术，不再仅依赖旧运行时临时替换。

首次迁移前字节备份保留于 `.art-source/scene-backups/`：菜单 `20261002-232149-996-630d7109/MainMenu.unity`，Arena `20261002-232303-170-8b987ec0/Arena.unity`，其 SHA256 与上方迁移前清单相同。旧 Match/Camera/Scene Systems 等层级保留在停用的 `Legacy v6 - Preserved inactive (not gameplay)` 下，不是新比赛权威。Build Settings SHA256 未改变。增量工具后续只修复引用和验证已有场景，不重建用户模型、不重涂已有主题。

正式基础验证保留于 `Tools/DashArenaIntegrationResults/20261002-formal-integration/`：

- `migration-2.log`：获批样板正式迁移成功；`migration-1.log` 保留首次检测把 inactive 旧组件误判为活动组件的失败记录及其修复来由。
- `hud-font-repair.log`：显式为新增正式 HUD 引用批准的 Noto TMP 字体，避免增量关场景后引用失效而回退旧字体；旧停用层级不重写。
- `editmode-1.xml`：471/471 通过，0 skipped，122.540 秒。
- `playmode-1.xml`：62/62 通过，0 skipped，16.525 秒。包括改版后的正式 Flow、菜单、网络暂停与个人 HUD；这不是自然完整局双进程或发行版性能证明。

退休旧测试与新断言的逐组映射见 `implementation-baseline.md` 后续附录；保留的旧模拟/旧协议测试只标 legacy，不重复记作新版证据。按实现和实际测试审核，3.1、3.2、4.2、4.3、5.3、6.4、7.1、7.2、8.2、10.1 已补记，阶段进度为 33/48。

2026-10-03 用户确认编辑器已关闭后继续批处理。第一轮新诊断工具定向测试在 `Tools/DashArenaIntegrationResults/20261003-diagnostics-presentation/targeted-1.xml`，18/18 通过、0 skipped：三种种子的普通生产零分自然驾驶达到实际双方 Dash/撞球/8–1 完局，另含确认事件记账和 v6/v7 故障报文分类测试。自然驾驶器此处为确定性模拟测试，尚未用发行包双进程验证。该次导入发现持久性测试新 `.meta` GUID 无效；该文件未被此轮发现，不能算测试覆盖，修正后单独复核执行。

PowerShell 报告校验器的纯合成样本也只属于校验算法测试；没有运行 Player 时不会据此宣称双端联机、真实得分或机制验收已通过。后续仍需多分辨率实际呈现、作者编辑持久性、机制双端诊断、完整新版本打包与验收。双电脑网络最终验证按已批准安排由用户与朋友配合，当前未执行。

### 完整 Unity 回归与新包（2026-10-03 后续）

- `20261003-diagnostics-presentation/editmode-1.xml`：512/512 通过，0 skipped，124.791 秒；实际发现并执行修复 GUID 后的作者持久性测试，含 22 项机制诊断/确认观察者/不合法初态防护及原有适用回归。
- `20261003-diagnostics-presentation/playmode-1.xml`：75/75 通过，0 skipped，23.841 秒；包括主机和加入方各自 12 种故障展示/冻结/手动返回、正式镜头四角限制及死亡高度、两个座位 HUD/真实输入推进、8 槽 FX 容量/36 Tick 回收与 epoch 清理。
- `Tools/DashArenaPresentationResults/20261003-140937-380dd95d-authored-persistence/authored-persistence.txt`：复制正式场景后保存自定义颜色、主题、比例、材质、新模型和手摆节点；重新加载、执行实际增量逻辑、两场景分别真实进入和退出 Play，再读盘复核全部保留。正式场景/Build Settings 的 SHA256 在测试期间不变；复制的测试资产明确位于 `Assets/PocketBumpDashArt/VerificationFixtures/`，不加入 Build Settings。
- `Tools/DashArenaPresentationResults/20261003-production-capture-1/`：保留首次失败记录。三个菜单标题框高度不足，另发现 URP UI Base camera 清背景覆盖了预合成世界；没有拿这种只有 HUD 的图片当完整场景通过证据。
- `Tools/DashArenaPresentationResults/20261003-production-capture-2/`：27 张真实场景/实际 TMP 渲染图，覆盖 1280×720、1920×1080、1024×768，1197 项检查通过、0 失败。菜单标题经显式备份后仅扩展框高，不改字体/颜色；世界捕获改为未经编辑的渲染结果作为临时 UI 背景。主线已实际打开分屏及 LAN 客座位图片确认世界可见。这些仍是明确摆放状态的离屏呈现 fixture，不是自然玩法或 Player 性能证据。
- PowerShell 校验器：`Tools/Test-LanDiagnosticEvidence.ps1` 的 49 项、`Tools/Test-LanMechanicEvidence.ps1` 的 414 项合成报告检查通过；覆盖两端同时缺失真实事件、计数伪造、错哈希、提前修复/复活、未实际注入等拒绝。其含义限于校验器算法，不能叠加到 Unity/游戏通过数。

新 Windows Development 包已构建到 `D:/uniry Assets/射球大战/PocketBump0.10/PhysicscollGame.exe`。构建前已核对最高既有版本为 0.9 且 0.10 不存在；不覆盖旧版本。`20261003-diagnostics-presentation/build-0.10.log` 记录 Succeeded、0 errors、0 warnings。构建时 `PhysicscollGame_Data/Managed/PocketBump.Runtime.dll` SHA256 为 `A64DF5018B68D57449B6BFB0DCFC3EC60826A0023D02DBEFA92EF6B86A951971`。

正在进行实际发行包同机双进程全套验收，索引 `Tools/DashArenaAcceptanceResults/20261003-141906-278-bee0d5/acceptance-index.json`。此时仅前三项 movement-smoke、handshake-first-loss、version-mismatch 完成；尚未完成时不可把整个 36 项套件标为通过。窗口/全屏及实际 Player 采样也仍单独待执行。阶段任务进度 42/48。

### 长测发现推进节拍缺口，等待设计确认

上述套件随后完成第 4 项自然完整局：`Tools/LanDiagnosticResults/20261003-142005-982-ad1ca6/` 两端最终 PASS，普通生产零分起局、实际双方 Dash/撞球/合法得分、确认终局、至少八秒健康结算、单边同意不启动、双边重赛到有真实输入/校验进展的新 Live 均通过。11.1 因而已完成，阶段任务进度 43/48。

第 5 项 `three-rematches-host-leaves` 在第一局早期失败，套件按默认规则停止，没有继续声称 36 项验收通过。失败证据完整保留于 `Tools/LanDiagnosticResults/20261003-142114-044-bcf4c3/`，主机与加入方最终报告、阶段日志及 Player 日志均存在：

- 加入方 `simulation_tick=455`、`last_remote_tick=442`、`confirmed_state_tick=442`，错误为 `INPUT HOLE`。
- 主机 `simulation_tick=441`、`last_remote_tick=456`，随后因诊断加入进程失败退出收到 `PEER LEFT`；不是玩家主动离开的规定阶段，因此不算成功离开证据。
- 双方发送错误、拒绝报文、实际故障注入和表现回调错误均为 0，双方 seed/rules/layout 身份一致。
- 主机已执行到 tick440，按照 2 Tick 输入延迟最多已生成本地输入442；加入方已完整确认到442，不是前缀中间漏包。加入方准备推进455时按 `455 - 12 = 443` 检查即将过期的输入；443 恰是较慢主机尚未生成的下一项，因此被误归为无法恢复的输入洞。
- `DashMatchDirector.FixedUpdate` 独立驱动每个进程，`DashLanLifecycle.CanAdvance` 在 Playing 不限制领先量；目前无比赛中节拍协调。此次证据不能判定具体 OS/渲染调度延迟来源，但已足以证明正确收发的两个进程仍可仅因 Tick 领先而结束。

候选修复是在传输/推进层给领先的一端增加有界等待，继续 Pump/ACK/原样历史重发，保持 12 Tick 回滚窗口、2 Tick 延迟及真实输入洞/独立超时规则，不把心跳当输入、不修改比分或弱化 PASS 条件。它改变现有 Playing 推进策略，按 apply 遇到设计问题先确认的规则，尚未实现或自动改写 proposal/design/specs。

`PocketBump0.10` 是保留的未通过完整验收候选，不是“联机已无问题”的最终交付。修复若获批准，后续完整构建应再查版本并顺延新目录（若无其他新版本，则 0.11），不得覆盖此候选及其证据。剩余两座位发行包机制、故障矩阵、连续重赛/双方离开重连、Player 窗口/性能采样与交付汇总仍未完成；双电脑测试未执行。

### 节拍修复获批并继续实施（2026-10-03）

用户回复“同意”，批准上一段提出的快端有界等待、继续网络收发、有效新输入自动恢复及 5 秒无进展后终局暂停。现有 proposal/design/rollback-netplay/lan-diagnostic-runner/visual-presentation 已记录该细化，tasks 新增 13.1–13.4，阶段进度改为 43/52（新增工作，不撤销历史验证）。OpenSpec strict 校验通过。

工程边界：输入采样前等待，不扩大 12 Tick 回滚窗口或改变 2 Tick 延迟；区分远端完整前缀之后的未生成输入与已有后续样本证明的真实中间洞；输入前缀没有前进时，心跳和重复历史不能延长独立输入期限。这里不承诺修复所有网络故障，也不把真实 Wi-Fi 验收视为完成。此前失败报告及 0.10 原包保持原样，0.11 目录经检查尚不存在。

## 交付使用与编辑入口

### 0.11 验证进展与保留失败

本轮 `Tools/DashArenaIntegrationResults/20261003-input-pacing/` 已保留：字体增量修复退出 0（原件备份 `.art-source/font-backups/20261003-160532-2c61419a/`）、完整 EditMode 521/521、诊断补强定向 14/14、完整 PlayMode 85/85，均 0 skipped。`20261003-input-pacing-capture/` 的 39 张显示夹具图/1833 检查/0 失败覆盖等待/超时中文和三种分辨率，正式场景与 Build Settings 哈希未改。PowerShell 校验器 49+414+144=607 项合成检查通过，不代表 Player 实测。

0.11 完整包构建成功、0 errors/0 warnings，未覆盖 0.10。实际 38 例套件索引 `Tools/DashArenaAcceptanceResults/20261003-161832-991-b98a8b/acceptance-index.json` 在第 4 例失败后停止；前三项普通 smoke、握手首包丢失、版本拒绝通过。失败 `Tools/LanDiagnosticResults/20261003-161933-021-34091a/` 中，Host Tick301 真停 45 个固定步、0.7501955 秒并持续 Pump；Join 在连续前缀302/Tick314等待约0.54秒，随后在 Tick315/前缀303恢复。双方确认 checkpoint495/hash1505812718 一致。但 Join 此后连续确认推进超过1800 Tick，校验计数一直停在302，诊断按30秒期限判 FAIL；Host 后续 PEER LEFT 是诊断 Join 退出的后果。

根因是初版安全领先阈值只保护了输入，未给滞后2 Tick的状态校验留空间，并非本例已证明 DESYNC 或发送失败。收紧新增 pacing 阈值至 10，保持 12 Tick/2 Tick及校验接受规则不变；新增长期非对称推进过程中持续校验测试。没有降低既定120校验恢复门槛。0.11 同样保留为完整验收未通过候选，下个修订包重查后顺延 0.12，不覆盖或抹去失败历史。

### 编辑与双电脑验收入口

修正 10 Tick 阈值后的完整 Unity 回归保留于 `20261003-input-pacing/editmode-2.xml`：533/533、0 skipped、295.318 秒，其中真实双 socket `DashLanSessionTests` 26/26。两角色各 1200 步 4:1 推进每 120 步必须新增至少 100 次校验；两角色各 360 轮精确触边等待/恢复逐轮检查校验历史仍可读取且双方验证增长，没有仅靠最后追平比较哈希。诊断因果判定 15/15 包含拒绝旧 12 Tick 领先值。PowerShell 独立校验器重新执行 154+49+414=617 项合成检查通过。13.1 已重新完成，实际新包矩阵仍待独立验证。

静态套件审核另发现诊断器自身假失败：重赛 Offer/Commit/Start 只由 Host 发出，Prepared/CommitAck 只由 Join 发出，但原最终检查要求每端都发生该故障注入。修正应根据真实发送角色要求实际注入，并由 PowerShell 独立复核，不修改原故障触发语义或以删除注入门槛取代验证。

该诊断修正已实现：5 个单向 profile 必须由规定一端实际注入，非发送端必须为 0；其余 9 个双向 profile 必须两端注入。报告的新 sender 字段只作声明，PowerShell 通过独立映射核验，不相信自报字段。`diagnostic-targeted-2.xml` 31/31、0 skipped（包含新增 16 例），完整 `playmode-2.xml` 85/85、0 skipped。纯 PowerShell 171+154+414=739 项通过，仍明确是合成校验器证据。

新完整 Development 包为 `D:/uniry Assets/射球大战/PocketBump0.12/PhysicscollGame.exe`，构建日志 `20261003-input-pacing/build-0.12.log`：Succeeded、0 errors/0 warnings，Runtime DLL SHA256 `A7C001E14B5F3C39A1E6EBA7546934603491EFE82519B1241833A79B35195A11`。新包实际 38 项验收索引 `Tools/DashArenaAcceptanceResults/20261003-164313-968-88c6d3/acceptance-index.json`；仅索引最终列出的完成项才算实测，不预先声称套件通过。历史 0.10/0.11 与失败证据保持原样。

两角色实际停步已通过：`20261003-164412-105-c86bee`（Host）/`20261003-164429-496-b81b1f`（Join）各冻结45固定步/约0.750秒且持续Pump；另一端在Tick313/前缀303真实等待约0.557秒，再Tick314/前缀304恢复。checkpoint494在各对内哈希一致，恢复后双方各新增189–191次验证。自然完整局`20261003-164447-128-ef2c7a`也通过：两端37个epoch0确认事件逐字段一致，普通输入实际9次Dash撞球进门、8:1、finishTick2663/checksum-1276300372；双方8秒健康结果等待、单边同意不启动、双边同意进入同连接新epoch1 Live。以上不代表尚在执行的剩余套件。

0.12 套件最终在第 25 项停止：24 项通过，包括全部网络场景、两个座位眩晕恢复。主机离开连续重赛 `20261003-164554-581-c45387`、加入方离开 `20261003-164854-294-77c4f5` 均真实完成三局、每局8秒健康结果等待、双边重赛、epoch3 Live后真实菜单离开和新连接Live。14项故障含5单向消息的实际指定角色注入；旧epoch两端各重放4包并拒收4包，静默负例双方明确 `PEER TIMEOUT`，不能把负例算作继续健康游戏。

失败保留在 `20261003-170758-157-d2ff88`：绿色 seat0 双方记录首次增强撞击 milestone Tick26，但后续碰撞覆盖对照字段至Tick171，严格PowerShell校验拒绝，尽管各Player自报PASS仍不能作为整案通过。这是诊断证据锁存错误，当前证据未显示游戏DESYNC；修复仅一次保存首次同确认帧成功对照，不修改绿色倍率或玩法，且新增跑至420 Tick的双座位持续回归。0.12及失败索引不覆盖，下一修订包再重查并顺延0.13。剩余12个机制用0.12独立预检，只用于及早发现其他问题，不代替最终新包验收。

0.12 的独立12机制预检从 `20261003-171024-032-3b6a70` 至 `20261003-171315-233-d6d84e` 均双端通过，未拼接成完整套件PASS。绿色记录锁存修复后，`mechanic-targeted-1.xml` 24/24、0 skipped，两座位持续420 Tick（确有后续绿色碰撞）不再覆盖首次证据；PowerShell进一步要求比较Tick恰等于首次增强碰撞里程碑，422+171+154=747项合成检查通过。`playmode-3.xml` 再次85/85、0 skipped、27.040秒。

新0.13完整构建成功（`build-0.13.log`，0 errors/0 warnings），Runtime DLL SHA256 `0D8C0C4815A34963E70D3C99C2D0A602126153EEFBDB28CB2BFD31FD9685DFD5`；旧0.12保持原样。当前先执行该新包绿色双座位和真实Player展示采样，再重跑完整38项，不提前认定最终交付通过。

0.13绿色双座位复测 `20261003-171801-102-633bea` / `20261003-171816-011-295ae9` 均双端最终PASS，持续到确认Tick300后comparison_tick仍为首次真实增强撞击26，严格同Tick校验通过。新的完整38项索引为 `Tools/DashArenaAcceptanceResults/20261003-172334-276-8dc5a3/acceptance-index.json`，未完成项不提前计入。

实际Player本地分屏三尺寸输出 `Tools/DashArenaPresentationResults/20261003-0.13-local-{1280x720,1920x1080,1024x768}/`，各600连续CSV样本、两相机各半、150/151 Tick进展、0异常/等待。1280首轮实际记录Windowed→FullScreenWindow→Windowed，主线及独立审查已查看原始PNG。每段只有约2.51秒，并非固定10秒测试，不能推断全玩法/通用性能。

LAN单视口初次隐藏窗口采样保留于 `20261003-171940-502-00d187`（1280）、`20261003-172049-560-2be20c`（1920）、`20261003-172158-697-dcae82`（1024）。虽探针/launcher自报PASS，实际逐图复核发现1280 Join PNG全黑，600样本仅0.1743秒/10Tick，其P50 0.2792ms不能算有效显示帧率；其他五张世界/HUD可见且座位正确。隐藏/遮挡导致未有效呈现是推断，现报告无OS窗口状态不能进一步断言。该组视觉验收判为不完整，当时12.3保持未完成并询问显示窗口许可；随后用户批准并完成下方可见复测。原始自报PASS文件不改写，以本审计记录纠正其证据范围。

截图完整性补强 `Tools/PlayerPresentationEvidence.ps1` 独立解码PNG、检查真尺寸/非单色/非近黑比例、600个有序有限正值CSV和重新计算P50/P95/max；它不启动进程、不显示窗口，也不能替代人工布局或性能判断。`Tools/Test-PlayerPresentationEvidence.ps1 -ValidateCurrent013Evidence`：46个合成检查通过，现有9报告只读检查接受8份并拒绝上述黑图。首次执行时尚未接入正在运行的网络launcher，避免中途改测试脚本。

实测设备条件为 i9-14900HX、RTX4060 Laptop GPU、约32GB内存、D3D11、驱动32.0.15.6094、Ultra、vsync=1、targetFrameRate=-1、runInBackground=true。以下每行均600帧；是Update帧间隔，不是独立GPU成本或通用FPS保证。LAN双进程与本地单进程负载不同，除首个本地采样外没有焦点。零等待、零异常，真实观测到的玩家拖尾峰值6，其余碎片/脉冲/拾取/弹窗峰值0，因此只是普通移动场景，不宣称复杂特效压力测试已完成。

| 模式 | 尺寸 | 秒数 | P50 ms | P95 ms | 最大 ms | 推进Tick | 图像 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 本地双视口 | 1280×720 | 2.508 | 4.163 | 4.171 | 8.264 | 150 | 正常 |
| 本地双视口 | 1920×1080 | 2.508 | 4.163 | 4.195 | 12.525 | 150 | 正常 |
| 本地双视口 | 1024×768 | 2.513 | 4.163 | 4.192 | 8.380 | 151 | 正常 |
| LAN主机 | 1280×720 | 2.500 | 4.163 | 4.194 | 4.207 | 150 | 正常 |
| LAN加入方 | 1280×720 | 0.174 | 不合格 | 不合格 | 不合格 | 10 | 全黑，排除 |
| LAN主机 | 1920×1080 | 2.508 | 4.163 | 4.195 | 8.336 | 150 | 正常 |
| LAN加入方 | 1920×1080 | 2.512 | 4.163 | 4.195 | 8.377 | 150 | 正常 |
| LAN主机 | 1024×768 | 2.504 | 4.163 | 4.194 | 8.373 | 151 | 正常 |
| LAN加入方 | 1024×768 | 2.504 | 4.163 | 4.194 | 8.353 | 150 | 正常 |

### 授权后的实际可见LAN复测（最终有效呈现证据）

用户明确回复“允许显示双窗口并自动测试”，之后才启用 `-CapturePresentation -ShowPresentationWindows`。没有更换运行代码、重新打包或覆盖失败图；launcher默认仍隐藏，未显式同时提供两个开关不会调整窗口。窗口辅助器仅使用本次Start-Process对象，复核进程启动时间、完整exe路径和Unity窗口归属，在可用屏幕内摆放，不控制其他程序窗口、不改变用户显示偏好。测试结束已确认无残留Unity或Player进程。

| 结果目录（`Tools/LanDiagnosticResults/`下） | 尺寸 | 主机P50/P95/max ms | 加入方P50/P95/max ms | 实际采样秒数（主/客） |
| --- | --- | --- | --- | --- |
| `20261003-175435-542-f18c63` | 1280×720 | 8.036 / 9.826 / 21.975 | 8.078 / 9.903 / 17.450 | 4.616 / 4.640 |
| `20261003-175545-827-4268b0` | 1920×1080 | 8.343 / 9.198 / 12.360 | 8.353 / 9.326 / 18.172 | 4.885 / 4.933 |
| `20261003-175655-287-3e9f8a` | 1024×768 | 8.326 / 9.391 / 15.576 | 8.325 / 9.513 / 17.300 | 4.954 / 4.972 |

三组launcher均exit0、主客最终PASS且confirmed Tick3599。每个 `host-presentation/` 或 `join-presentation/` 下有600帧原始CSV、JSON及未编辑PNG；逐图主线/独立审查确认世界和HUD可见、P1/P2归属正确、各一个完整视口，CSV百分位独立重算误差小于0.000002ms。实际采样分别推进277/278、293/296、297/298 Tick，所有等待增量、warning/error/presentation error均0。`presentation-windows.json`记录客户区尺寸、PID、启动时间、exe路径、UnityWndClass、可见且未最小化以及摆放矩形；两窗可部分重叠，但没有完全相互遮挡。

每段约4.6–5秒，是同机双Player普通移动/Dash场景。实际玩家拖尾峰值18且未越界，其他脉冲/碎片/拾取/弹窗峰值0，不能声称已实测满特效压力。容量和回收另由PlayMode案例验证。OS可见性是布置时快照而非全程监控；上述Update帧间隔含调度/vsync，不是GPU计时。本地分屏是单进程且采样时聚焦/遮挡条件不同，**不能用两组数值直接推导分屏开销或“全机器稳定120/240FPS”**。

### 可编辑入口与实际双电脑测试

- 正式场景：`Assets/Scenes/MainMenu.unity` 和 `Assets/Scenes/Arena.unity`；Arena 的活动根为 `Dash Match - Authored v7`。旧 v6 内容停用保留，不是当前玩法；不要重新启用旧 director/camera。
- 修改机器人颜色：`Assets/PocketBumpDashArt/Materials/P1 cyan shell.mat`、`P2 tangerine shell.mat`；场地为 `Warm ivory tiles.mat`、`Fresh mint.mat` 等序列化材质。可在正式场景对象的 Renderer 检查实际引用，颜色与碰撞规则无关。
- 修改/添加机器人外观：`Assets/PocketBumpDashArt/Prefabs/Robot_Cyan.prefab`、`Robot_Orange.prefab` 的 `Motion` 视觉子层级；保留已绑定的运动/四肢/状态显示节点。纯装饰无需新增刚体或碰撞器；可见模型尺寸不会自动改变自有模拟的圆形碰撞边界。
- 柱、墙、自由球与三色 Buff 的可编辑 Prefab 也在上述 `Prefabs` 目录。新增有真实玩法作用的障碍应走 `ArenaRulesProfile` 布局/身份验证，不能仅在场景手摆一个 Unity Collider 后假定联机模拟已经识别。
- 菜单主题源为 `Assets/Resources/PocketBump/DashUiTheme.asset`；已有 UI 保存作者颜色，不会在 Awake 强制全量重涂。调整已保存界面时检查其 Image/TMP 引用，主题配置不是强制运行时换色器。
- 字体及再分发许可：`Assets/PocketBumpDashArt/Fonts/`，来源与加工记录见本变更 `resource-candidates.md`。本版没有新增音效或下载机器人模型。
- 同机本地：开始本地比赛后左 P1（WASD、默认 K Dash）、右 P2（方向键、默认小键盘 2 Dash）；已保存自定义 Dash 键继续有效。LAN 两端各用本机 P1 键位控制自己协商到的座位，每端单视口。

双电脑 LAN 仍需用户和朋友按已确认安排验收：复制**同一完整新版本目录**到两台电脑；主机 Host LAN，加入方 Join LAN 输入主机局域网 IPv4（跨电脑不能填 127.0.0.1）；允许该游戏的 Windows 专用网络通信。先正常移动/Dash/撞球到完整终局，再双方同意重赛，分别测试任一端主动返回主菜单后重新建房加入。出现等待或终局故障时保留右上原因、两端版本、发生时刻、当时操作及两端 Player 日志/可用报告，不把问题简化为“又退出了”。不要关闭整个防火墙或拿不同版本的单个 exe 混搭。自动 loopback 工具只用于同一电脑，不能把它的共享本地协调目录直接当作跨电脑验收工具。

### 本机复测命令

完整新包的快速双进程移动/握手测试（从任意 PowerShell 工作目录运行）：

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Unity project\PhysicscollGame\Tools\Run-LanDiagnostics.ps1" -PlayerPath "D:\uniry Assets\射球大战\PocketBump0.13\PhysicscollGame.exe"
```

全部 38 个案例（耗时明显长于 smoke；默认首个失败停止并保留证据）：

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Unity project\PhysicscollGame\Tools\Run-DashArenaAcceptance.ps1" -PlayerPath "D:\uniry Assets\射球大战\PocketBump0.13\PhysicscollGame.exe" -Suite Full
```

只重现此次获批的停步/等待/恢复验证，可给快速命令追加 `-Scenario pacing -PacingStallRole host` 或 `join`。主客端在同一电脑共享诊断协调目录，工具明确区分主动停步和真实网络丢包；正常玩家运行不启用此场景。结果会创建新的 `Tools/LanDiagnosticResults/<时间戳>/`，完整套件另保存 `Tools/DashArenaAcceptanceResults/<时间戳>/acceptance-index.json`；保留整份主客报告、阶段日志和 Player 日志，不仅复制单独的 PASS 字样。

需要看见两扇自动测试窗口并保存实际画面时，显式使用（结束后工具自动关闭本次窗口）：

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Unity project\PhysicscollGame\Tools\Run-LanDiagnostics.ps1" -PlayerPath "D:\uniry Assets\射球大战\PocketBump0.13\PhysicscollGame.exe" -CapturePresentation -ShowPresentationWindows -ProbeSize 1280x720 -ProbeFrames 600 -TimeoutSeconds 240
```

尺寸还支持 `1920x1080`、`1024x768`。屏幕可用区域装不下指定尺寸时工具明确失败，不通过缩放或放到屏幕外伪造验收；不带展示开关的原smoke命令不受此窗口要求影响。PNG/CSV完整性校验会拒绝缺失/损坏/黑图，不把报告自报PASS当作足够证据。

### 随包资源许可

0.13 已补入 `ThirdPartyNotices/`：Noto Sans CJK SC 的完整OFL和版权来源、现有Liberation Sans OFL、Kenney来源说明和七包原始许可，共11文件。由 `Tools/Copy-PocketBumpReleaseNotices.ps1` 固定来源复制并逐份SHA256核验；运行程序集哈希仍为上面的 `0D8C0C...685DFD5`，没有修改游戏内容或历史版本。工具的61项纯PowerShell夹具测试通过，已存在不同字节时拒绝覆盖，同字节重跑只校验。将来创建新版本后也应执行该分发步骤（替换目标为实际新版本）：

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Unity project\PhysicscollGame\Tools\Copy-PocketBumpReleaseNotices.ps1" -BuildDirectory "D:\uniry Assets\射球大战\PocketBump0.13"
```
