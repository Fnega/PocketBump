# 射球大战 / PocketBump

一款双人 3D 俯视角碰撞竞技游戏。驾驶圆润小机器人，用移动和 Dash 把自由滚动的球撞进对方球门；也可以撞开对手、争抢 Buff，借助障碍物改变攻防节奏。

开发者：**Fnega** · 当前试玩版本：**1.2** · 平台：**Windows x64**

支持同电脑双人左右分屏，以及同一局域网双人对战。当前没有互联网房间、自动匹配或中继服务。

## 下载与运行

试玩版本与发布状态请查看 [GitHub Releases](https://github.com/Fnega/PocketBump/releases)。可下载版本会附带 `PocketBump1.2.zip`；GitHub 自动生成的 **Source code** 压缩包是源码，不是可直接运行的游戏。

1. 完整解压 `PocketBump1.2.zip`。
2. 打开 `PocketBump1.2` 文件夹，运行 `PocketBump.exe`。
3. 同机对战选择 **START**；局域网对战使用 **HOST LAN / JOIN LAN**。

不要只复制 EXE。`PocketBump_Data`、`UnityPlayer.dll`、`ThirdPartyNotices` 等文件必须一起保留。游戏默认以 1280×720 窗口运行，设置中可切换窗口/全屏和调整音效音量。

1.2 ZIP 的 SHA256：

```text
6EDB9C5DF4725914C767B3155893B77AFD103310C15FB4BE9C1C33420CE98F61
```

Windows PowerShell 可用 `Get-FileHash .\PocketBump1.2.zip -Algorithm SHA256` 核对。包内的 `SHA256SUMS.txt` 提供逐文件校验值。

## 怎么玩

- **碰撞得分**：没有接球、持球或蓄力射击；直接撞击白球，将它推进对方球门。蓝方守北门，橙方守南门。
- **Dash**：持续 0.3 秒，按下时确定方向，期间不能转向；结束后冷却 3 秒，需要再次按键触发。没有方向输入时沿当前朝向冲刺。
- **掉落与开球**：掉出场地不给对手加分，通常 3 秒后在安全位置复活；进球后双方归位、重新开球。
- **场地互动**：撞柱会短暂眩晕；高速 Dash 可以撞碎墙，10 秒后在位置空闲时修复。
- **胜利条件**：默认目标 8 分，达到目标且领先至少 2 分时获胜；开局前可调整目标分数和球数量。
- **小地图**：右上角显示双方位置、朝向、得分球、球门和障碍物。

| Buff | 效果 |
| --- | --- |
| 绿色 | 最多 5 秒撞击强化 |
| 紫色 | 最多 5 秒霸体，免击退和眩晕，但仍可能掉落 |
| 蓝色 | 立即随机传送到安全位置；没有安全位置时消耗拾取但不传送 |

掉落或进球会清除绿色、紫色 Buff。拾取后会显示简短效果说明。

## 键位

| 模式 / 玩家 | 移动 | 默认 Dash |
| --- | --- | --- |
| 本地 P1 | W / A / S / D | K |
| 本地 P2 | 方向键 | 小键盘 2（Num2） |
| LAN 本机玩家，两端相同 | W / A / S / D | P1 的 Dash 绑定，默认 K |

**Num2 是小键盘 2，不是主键盘数字行的 2。** 冲刺键可在开局前重新绑定；没有小键盘时也可自行选择其他按键。

升级不会覆盖已有有效绑定：如果以前保存了主键盘 `2`，升级后仍使用它，以规则面板显示为准。

按 **Esc** 打开比赛菜单：本地模式暂停；LAN 模式不暂停比赛，本机暂不接收移动和冲刺输入。菜单提供继续、玩法说明、音量、报告目录和返回主菜单。

## 局域网连接

两台电脑请使用**同一个完整试玩包**，不要混用不同版本的程序和数据目录。

1. 两台电脑连接同一路由器或可互通的局域网。
2. 加入者选择 **JOIN LAN**，填入主机的局域网 IPv4 地址。
3. 主机选择 **HOST LAN** 并开始建房，加入者随后确认加入。握手超时后请双方返回菜单重新连接。
4. 两端的目标分数和球数量需要一致。加入者如需调整，先在 **START** 的规则面板设置，再返回 **JOIN LAN**。

默认使用 **UDP 7777**。若 Windows 提示网络权限，请按自己信任的网络范围授权，不需要关闭整个防火墙。访客 Wi-Fi、校园网络或虚拟网络的设备隔离可能影响直连。

同电脑双窗口测试：运行两次游戏，一窗建房，另一窗加入 `127.0.0.1`。**这个地址只适用于同一台电脑**，朋友电脑应填写主机的实际局域网地址。

1.2 保持协议版本 8；1.0 使用协议 7，不能与 1.2 联机。当前没有自动重连。

## 1.2 的改动与验证范围

- 隐藏短暂“等待远端输入”面板，保留输入缓冲、自动恢复、诊断记录及真正终止故障的提示。
- 本地 P2 的全新默认 Dash 键改为 `Num2`，保留已保存的有效自定义绑定。
- 改进正式包验收工具：增量读取本轮报告、提前填写加入地址，避免历史报告扫描耗时挤占握手窗口；没有延长游戏超时。

本轮受影响的 EditMode 测试 **63/63**、全量 PlayMode 测试 **158/158** 通过。实际非 Development 包已完成本机双窗口连接、交换主机重连、LAN 菜单继续运行、主动退出提示，以及本地控制和暂停恢复检查。发布文件与已验收候选逐项比对。

**这些结果不等于真实双电脑校园网已验收，也不保证所有网络都不会中断。** 更完整的说明和双电脑复测步骤随试玩包提供。

## 从源码打开项目

建议使用与当前构建一致的 **Unity 2022.3.62f3c1**，并安装 Windows 构建支持。项目使用 URP；包版本记录在 `Packages/manifest.json` 和 `Packages/packages-lock.json`。

```powershell
git clone https://github.com/Fnega/PocketBump.git
```

请切换到包含本 README 的版本或分支；如果从合并请求预览，请使用对应的发布分支。

在 Unity Hub 中添加包含 `Assets`、`Packages`、`ProjectSettings` 的项目根目录，等待资源导入和依赖恢复。Git 类型依赖需要可用的 Git 和网络访问；不要随意升级锁定依赖来跳过报错。

打开 `Assets/Scenes/MainMenu.unity` 后运行。构建场景已配置为：

```text
Assets/Scenes/MainMenu.unity
Assets/Scenes/Arena.unity
```

场景、美术和字体已有可编辑资源。**仅为打开或打包项目，不需要执行场景/美术烘焙工具**，以免覆盖自己的编辑。

### 目录索引

| 目录 | 用途 |
| --- | --- |
| `Assets/Scripts/PocketBump/` | 游戏规则、输入、表现、菜单和联机逻辑；当前玩法主要为 `Dash*` 类 |
| `Assets/Scenes/` | 主菜单、正式竞技场和开发样板场景 |
| `Assets/PocketBumpDashArt/` | 机器人、美术材质、字体、音效来源说明等 |
| `Assets/Tests/EditMode/`、`Assets/Tests/PlayMode/` | 自动化测试 |
| `Assets/Editor/` | 编辑器制作、验证和构建入口 |
| `Tools/` | PowerShell 验收、诊断和发布工具 |
| `Tools/Release/1.2/` | 试玩说明、双电脑验收步骤与验证范围 |
| `Tools/ReleaseNotices/` | 发布声明所需的原始许可证和来源记录 |
| `openspec/` | 需求、设计与变更记录 |

### 测试与构建

Unity 中使用 **Window → General → Test Runner**，分别运行 EditMode 和 PlayMode 测试。运行批量 Unity 测试/构建前，请先保存并关闭同一项目的编辑器，避免进程冲突。

当前 Release 构建入口为 `PocketBump.Editor.PocketBumpBuild.BuildWindowsRelease`，Development 构建入口为 `PocketBump.Editor.PocketBumpBuild.BuildWindowsDevelopment`；均通过 `-pocketBumpOutput` 指定**新的输出目录**，已有目标会被拒绝覆盖。

- `Tools/Run-LanDiagnostics.ps1`：用于单独的 **Development 诊断包**，不能把内部测试参数用于正式试玩包。
- `Tools/Run-ReleaseDemoAcceptance.ps1`：通过普通界面操作正式包，需要显式指定 `-ShowTestWindows`。它会显示游戏窗口并使用键鼠；运行期间不要操作电脑或切换窗口。
- `Tools/Test-ReleaseReportObserver.ps1`、`Tools/Test-ReleaseDemoAcceptanceSequence.ps1`：报告读取和连接时序的纯 PowerShell 回归，不启动游戏。
- `Tools/New-PocketBumpReleasePackage.ps1`：只打包与成功验收清单一致的候选文件，并核对 ZIP 内容和第三方声明。

发布声明工具需要 Unity 先恢复锁定版本的 `Library/PackageCache`。不要把 `Library` 缓存、私人报告或本地构建目录提交进源码仓库。

## 报告问题

游戏菜单可打开本机报告目录，默认位置为：

```text
%USERPROFILE%\AppData\LocalLow\Fnega\PocketBump\NetworkReports
```

报告不会自动上传。反馈时请说明游戏版本、连接方式、复现步骤、发生时间，以及两端画面；网络问题最好保留双方相同时段的 JSON 和必要的 `Player.log`。报告中可能包含局域网端点和本机路径，**公开提交 Issue 前请检查并脱敏**，不要直接提交整个报告目录。

问题可提交到 [GitHub Issues](https://github.com/Fnega/PocketBump/issues)。

## 素材与许可

本项目目前尚未指定统一的项目源码许可证，本文不会把第三方素材的许可视为整个项目的许可。

- Kenney 素材的来源和原许可位于 `Assets/Resources/PocketBump/Art/Kenney/`。
- Noto 中文字体的原许可和来源说明位于 `Assets/PocketBumpDashArt/Fonts/`。
- TextMesh Pro 所含 Liberation Sans 字体保留其 OFL 文件。
- Unity 依赖及其他组件的声明由发布工具整理到试玩包的 `ThirdPartyNotices/`。
- 项目模型和自行合成短音效的来源记录位于 `Assets/PocketBumpDashArt/`；当前没有背景音乐。

使用、修改或分发第三方内容时，请同时查阅并保留其各自的原许可和来源声明。

## 已知限制

- 当前以 Windows x64、键盘操作、本地双人和局域网对战为目标；尚未完成最低配置和多机型覆盖测试。
- 校园网长期稳定性仍需要真实双电脑复测；暂时隐藏等待面板不意味着网络等待消失。
- 非 16:9 屏幕全屏可能出现上下黑边，以保持画面比例。
- 试玩构建环境中曾出现 `trial version` 标识；项目维护者已确认此构建允许公开分发，发布过程没有修改或绕过授权配置。
