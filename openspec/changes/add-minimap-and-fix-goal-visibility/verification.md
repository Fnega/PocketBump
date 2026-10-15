# 小地图与球门可见性实施记录

## 基线（2026-10-07）

- 用户确认：靠近蓝方门仍不可见，LAN也出现；地图北朝上、右上角、本地每半屏一张/LAN一张，球、双方位置朝向、门及障碍；网络提示改右下，不隐藏地图。
- 当前最高完整包0.13。其Runtime DLL SHA256：`0D8C0C4815A34963E70D3C99C2D0A602126153EEFBDB28CB2BFD31FD9685DFD5`。尚未创建本次新包，预期顺延0.14，打包前再查。
- 正式Arena SHA256：`422F201EDDB3B641C04E5991C6F113A6FB6C052388C9FC242F3FE5EE619DC17C`。
- 正式MainMenu SHA256：`BA23AE5CB646A760D3B06DCD553E3EB1CE22DAE51BE1F69CCA8146307EE2188B`。
- BuildSettings SHA256：`905DF335CC8817706849000764C8FC2E4769CD71AF236190CF8CBA8C4594AFC9`。
- 工作树已包含上一变更大量修改/未跟踪文件（包括Arena、Dash HUD/Presentation）；全部按既有用户工作保留，不reset或广泛覆盖。当前读到的已完成`redesign-dash-collision-arena`为52/52，未归档。本变更不改写其验收记录。
- 本轮CIM只读检查未发现Unity或PhysicscollGame进程；批量操作前仍需重新检查，若用户重新打开则先保存/关闭而不强制结束。

## 根因与测试边界

`DashArenaPresentation.UpdateCamera`将正交镜头中心限于z≤23.4（半场36，halfHeight10.5，margin=12.6）。偏移(0,20,-12)下，蓝门横梁中心(0,2.42,36.2)的viewportY约1.08195，门线地面Y约1.01450，均超出上边；橙门横梁则约0.03663。正式场景蓝门对象及Renderer启用，并非模型删除。旧相机测试只重复边界公式，未证明完整门框在HUD安全区可见。

此结论来自源码/场景和投影数学核对；本次新的Unity实测结果应在下方单独登记，不把旧551/85/38通过记录当作新功能已通过。

## 规范依赖

新增arena-minimap；visual-presentation的网络提示位置修改以尚未同步的Dash改版完整Requirement为基线。后续同步先处理改版再应用本变更，避免旧右上要求覆盖新右下布局。此轮不自动同步或归档。

## 实施与新验证结果

只读地图/相机不得改变生产规则身份、协议或碰撞；显示夹具截图与实际双Player网络测试分别记录，双电脑LAN未在本轮执行。

### 2026-10-07 定向测试（保留失败证据）

- `Tools/MinimapResults/20261007-validation/targeted-playmode-r2.xml`：76 项，64 通过、12 失败。其中相机/表现 52/52、网络 HUD 10/10；小地图 2/14。
- 9 项地图布局测试在 kickoff 尚未完成时错误期待 8 个活动球；测试已改为通过真实模拟/双端会话推进到 Live，未直接篡改回滚状态。
- 3 项网格测试反射调用撞上继承的同名重载；已明确选择 `OnPopulateMesh(VertexHelper)`，保留原严格断言。
- 另补 4 项端区内 1 单位传送/复活事件测试，避免大距离自动重置掩盖事件插值问题。以上修正后的 Unity 测试尚未重跑，不记录为通过。

### 2026-10-11 继续检查与暂停点

- 复核 Arena、MainMenu、BuildSettings 的 SHA256 均与上述基线相同；尚未迁移正式场景，也未构建 0.14，现有最高版本仍为 0.13。
- 首次预检仅凭 PID 50920 的进程记录阻止启动，该判断不充分。用户指出编辑器未运行后复核：该记录无窗口、0 线程、0 句柄、无可读命令行/执行路径，项目 `Temp/UnityLockfile` 不存在，符合残留记录而非活动编辑器。未终止该进程、未删除任何锁文件；撤销暂停并正常启动定向测试（PID 38952）。
- `Tools/PlayerPresentationEvidence.ps1` 新增向后兼容的 `-RequireMinimap`。新版本验收必须显式启用，检查角色/座位、地图数、数组长度、实体计数、有限正矩形及各自右上区域；默认模式仍可复查旧包。
- `Tools/Test-PlayerPresentationEvidence.ps1`：125 项合成证据检查通过；`-ValidateCurrent013Evidence` 只读接受 8 份旧正常报告、正确拒绝 1 份保留黑图。这是验证器回归，不代表新版 Player、渲染或网络已验收。
- 下一步：关闭编辑器后重跑定向测试，成功后增量备份/保存 Arena，再执行完整 Unity 回归、显示夹具截图、新版本打包与获准的可见窗口测试。

### 2026-10-11 新验证进展

- `Tools/MinimapResults/20261011-validation/targeted-playmode.xml`：80/80 通过，0 失败/跳过。包含 56 项相机/表现、14 项地图、10 项网络 HUD 测试；原 12 项失败均已复测通过。
- `integration.log`：增量更新成功，精确备份位于 `.art-source/backups/minimap/20261011-155237-208-6e2f54f2/`。4048 个既有对象/组件、148 项 Graphic 样式、23 份引用材质文件通过保护检查；第二次安装为 no-op。
- 更新后 Arena SHA256：`3F57231A6E9D9C890EF86ABF224D0B9D30E23F490556802AE374A457CC88F9D5`。MainMenu、BuildSettings、Arena meta 均未改变。
- `full-editmode.xml`：554/554 通过，0 失败/跳过；`full-playmode.xml`：134/134 通过，0 失败/跳过。完整测试不包含实际两台电脑网络验收。
- `Tools/MinimapResults/20261011-display-fixtures/` 保留首轮 60 张图和 18 项失败：图像工具仅采样中心区域，漏过南门近端画面底部的球门/角色。修正为全画面均匀网格取样，保留原有 >12 色阈值并增加非黑像素检查；未修改玩法或显示布局来迁就检查。
- `Tools/MinimapResults/20261011-display-fixtures-r2/capture-report.json`：60 张图、2603 项检查全部通过。覆盖三尺寸、Local/HostLan/JoinLan、两门、8 单位/0.25 单位接近距离、网络等待/错误，8 球与墙状态。抽查本地北门、LAN 两座位近门及南门故障/北门等待画面，门框完整、小地图右上、提示右下、中文无溢出。全部是明确标记的离屏显示夹具，非真实网络故障或性能证据。

## 编辑器入口与复测方法

- 打开 `Assets/Scenes/Arena.unity`，找到正式 `DashMatchCanvas/Seat0/Minimap` 和 `Seat1/Minimap`。`DashArenaMinimap` 中可调地图/玩家/球/墙颜色，RectTransform 可调尺寸与位置；`NetworkIssuePanel` 是右下网络提示。运行时不会重建整套 HUD 或重复执行迁移。
- 地图北朝上：青/橙箭头是双方方向，白圈标本地玩家，白点是活动得分球，橙色空心墙为待修复。死亡玩家和未活动球不冒充活动标记。
- `Tools/PlayerPresentationEvidence.ps1` 的新包校验必须加 `-RequireMinimap`；同时检查实际 PNG，JSON 校验不是人工画面检查的替代。
- 真实双电脑 LAN 仍需双方使用同一完整版本，跑一局并收集两端报告；本机双进程不会被标成双电脑验收。

可重复的同机双窗口检查（会短暂显示窗口、自动保存日志并只关闭本轮进程）：

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Unity project\PhysicscollGame\Tools\Run-LanDiagnostics.ps1" -PlayerPath "D:\uniry Assets\射球大战\PocketBump0.14\PhysicscollGame.exe" -CapturePresentation -ShowPresentationWindows -ProbeSize 1280x720 -ProbeFrames 600 -TimeoutSeconds 240 -HostStartupTimeoutSeconds 60
```

校验输出目录中的 `host-presentation` / `join-presentation`（替换下例报告目录）：

```powershell
. "D:\Unity project\PhysicscollGame\Tools\PlayerPresentationEvidence.ps1"
Assert-DashPlayerPresentationEvidence -Directory "<本轮报告目录>\host-presentation" -ExpectedWidth 1280 -ExpectedHeight 720 -ExpectedFrames 600 -RequireMinimap
Assert-DashPlayerPresentationEvidence -Directory "<本轮报告目录>\join-presentation" -ExpectedWidth 1280 -ExpectedHeight 720 -ExpectedFrames 600 -RequireMinimap
```

## 新完整包与实际 Player 证据

- `D:/uniry Assets/射球大战/PocketBump0.14/`：Windows Development 构建成功，0 errors / 0 warnings，11 份第三方许可证复制并逐份 SHA256 校验。Runtime DLL SHA256：`AD6137D74F02222F921F000CDD79DA19627AE5A15BE5DC5B410664E33CDFB72D`。
- 原 0.13 Runtime DLL SHA256 复查仍为上述基线，未覆盖旧包。截图/测试结束后的正式 Arena、MainMenu、BuildSettings 哈希也与迁移后预期一致。
- 本地实际窗口：`Tools/MinimapResults/20261011-0.14-local-{1280x720,1920x1080,1024x768}/`。三组各 600 帧、两相机/两地图、0 错误；普通输入驱动双方到 `(0,±32)` 附近，严格检查实际位置而非直接摆放角色；均通过 `-RequireMinimap` 和 PNG 完整性检查，人工查看三张最终 PNG 确认完整球门及各自右上小地图。
- 帧间隔仅是该开发包在本机的观察值（本地三组 p95 约 4.17–4.19ms），不是 GPU 基准或其他机器性能保证。
- 实际 LAN 双窗口三组均 PASS（每组双方推进至 3600 Tick，终止原因空、send_failures=0）：
  - 1280×720：`Tools/LanDiagnosticResults/20261011-160737-674-31012f/`
  - 1920×1080：`Tools/LanDiagnosticResults/20261011-160849-141-9a77ba/`
  - 1024×768：`Tools/LanDiagnosticResults/20261011-161000-996-88b19a/`
- 六份实际 Player 报告各 600 帧、单相机/单地图，Host seat0、Join seat1，0 日志错误/表现回调错误。全部通过显式 `-RequireMinimap`、CSV/PNG 完整性校验，人工查看六张最终画面，小地图正常、无黑图。窗口身份/尺寸/可见状态见各组 `presentation-windows.json`；启动器只清理本轮启动的进程。
- 本轮 LAN 证据是普通移动/Dash 的 smoke，不是完整对局/进球链、故障注入或真实双电脑。规则哈希 `7012a8bc484f3d522ace17ccc550c9572659c5d0d2f96fa122a83fa27831abb8`、布局哈希 `13a8a46bb386b63ff20e427546d184a5eb932274c753ea8d6397e69c9dcaa500` 和协议7未改变。
- 交付前复核六份 LAN 采样报告均为 0 warnings / 0 errors，相关 Player 日志未检出 NullReferenceException、SocketException、DESYNC。无 PhysicscollGame 进程残留；只见此前 PID50920 的 0 线程/0 句柄/无窗口记录，未对其执行终止操作。
- 本变更严格 OpenSpec 校验通过，实施任务 10/10；未自动提交 Git、同步主规范或归档。既有失败报告及旧版本保留。
