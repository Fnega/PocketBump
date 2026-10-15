# 现有资源盘点与已确认的小样制作路线

记录日期：2026-10-02。最初仅盘点本机已有资源；随后按用户单独批准新增一款中文字体，详情见下。未购买或下载新模型/音效；此记录不是 G3 美术验收。

## 已有资源

| 内容 | 实际位置（项目根目录下） | 用途及限制 |
| --- | --- | --- |
| Kenney 原始包 | `.art-source/kenney/downloads/` 与 `packages/` | 原始 ZIP 与完整解压源保留，不覆盖、不直接作为 Unity 运行目录 |
| 来源与逐包许可 | `Assets/Resources/PocketBump/Art/Kenney/THIRD_PARTY_NOTICES.md` 与 `Licenses/` | 七包来源已登记，各包附 CC0 文本；本记录以随包许可证为依据，不代替新外部素材的审核 |
| Marble 球体 | `Assets/Resources/PocketBump/Art/Kenney/Models/Marble/marble-low.fbx` | 可作自由得分球的视觉候选；球的权威半径仍读取 profile |
| Space 场景模块 | `Assets/Resources/PocketBump/Art/Kenney/Models/Space/` | 可参考几何或作小样比较，现有造型/纹理不能直接等同于已批准的清新机器人风格 |
| Particle Pack / Light Masks | `Assets/Resources/PocketBump/Art/Kenney/` 中已导入粒子图与 Light Masks | 可试作浓色 Dash 流光、星星、护罩与冲击；需独立材质，不覆盖旧共享资源 |
| Input Prompts / UI / Patterns | 同一 Kenney 目录及源包 | 可按新 Dash 操作筛选，旧射击/充能提示不能沿用为新动作 |

首次盘点时没有可直接采用的圆润机器人模型或绑定动画，七个包与 Assets 中没有可确认使用的音效。原有 Liberation Sans 不覆盖完整中文，灰盒用本机字体回退不代表发行包已解决字体问题，不能直接复制系统字体作分发；新增字体的来源与批准如下。

## 已确认的制作路线（2026-10-02）

用户选择“采用这个方案（推荐）”：原创可编辑圆润机器人、复用现有 CC0 特效贴图、第一版静音，不下载新模型；可随游戏分发的中文字体另列来源再请用户确认。以下只批准独立小样制作，不批准正式场景替换。

- 角色：用项目原创的球/圆柱/盒状网格组合圆头、面罩、躯干和短手脚，先做青蓝/橙两款可编辑 Prefab；成本是比例调校和程序动画制作，不需要购买或下载模型，但不承诺达到精修骨骼动画资产的细节。
- 场景：原创基础几何与独立米白/薄荷材质，碰撞脚印由同一 profile 驱动；墙体碎片仅作视觉，不能引入第二套 PhysX 权威。
- 特效：优先复用上述已有许可的贴图，独立保存小样材质与效果对象；正式采用前展示真实 Unity 画面，而非仅概念图。
- 音效：本轮按用户确认保持静音，不引入新声音；中文字体按下述单独确认处理。

小样加工资源生成位置：`Assets/PocketBumpDashArt/`；独立场景为 `Assets/Scenes/DashArenaArtSample.unity`。原创网格/材质/Prefab 及项目已有 CC0 贴图的方案不产生第三方购买费用；原始 Kenney ZIP/授权仍留在原位置，不覆盖或重下载。工具只创建不存在的目标，独立样板不得覆盖 `MainMenu.unity`、`Arena.unity`。当前 `DashArenaGreybox` 仅是机制灰盒，不是此美术样板。

## 未通过的门槛

1. 原创机器人及静音小样制作路线已获批；中文字体已单独选定、下载并通过 Unity 151 个字符/四种字号的首次字形检查，发行包验证仍待执行；新增音效不在本轮样板范围。
2. 已生成独立机器人 Prefab、主题材质和实际 Unity 小样截图。2026-10-02 用户在看到 `Tools/DashArenaArtSampleResults/20261002-reviewed-sample/` 后回复“符合预期”，已完成 G3 造型/配色确认。此版以原创网格和程序绘制的线/环/符号表现效果，未另行下载贴图，也未改动现有 Kenney 原资源。
3. 正式场景美术替换与发行验收不得依据本盘点标记完成。

## 单独批准的中文字体

用户选择“采用并下载这个字体（推荐）”，批准 Noto Sans CJK SC Regular。官方来源 [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk/blob/main/Sans/README.md)，[OFL 1.1 许可](https://github.com/notofonts/noto-cjk/blob/523d033d6cb47f4a80c58a35753646f5c3608a78/LICENSE)，无购买费用。下载固定为 `Sans2.004` 提交 `523d033d6cb47f4a80c58a35753646f5c3608a78`，未下载其他字重或字体包。

原始字体及完整许可证存放 `.art-source/fonts/noto-cjk/Sans2.004/`；Unity 字体、许可和来源说明存放 `Assets/PocketBumpDashArt/Fonts/`。原文件与副本 SHA256 一致，字体 `2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b`。版权来自字体内部 name 表，完整保留在 `THIRD_PARTY_NOTICES.md`。没有复制、分发系统雅黑/黑体；后续 TMP 图集和发布包应保留来源及许可。
