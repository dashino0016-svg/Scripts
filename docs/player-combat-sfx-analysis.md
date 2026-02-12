# 战斗音效脚本结构分析（通用角色版）

## 1. 核心模块

- **事件总线层**：`Public/CombatSfxSignals.cs`
  - 提供通用事件：攻击挥空（whoosh）、命中结算（hit resolved）、能力触发、能力3慢时间BGM开始/结束。
  - 通过 `Raise...` 入口解耦“战斗逻辑触发点”和“音频播放实现”。

- **类型与映射层**：`Public/CombatSfxTypes.cs`
  - 使用 `CombatAttackSfxKey`（`Group + Variant`）描述攻击音效键，不再硬编码 A1~A4 / B1~B4 固定枚举。
  - `CombatSfxKeyUtility.TryGetAttackKey(...)` 把 `AttackData.sourceType` 映射到通用分组（ComboA/B、HeavyA/B、SprintA/B）。

- **配置层**：`Player/CombatSfxConfig.cs`
  - 采用 `ScriptableObject` 承载音效配置。
  - `AttackSfxEntry` 以 `group + variant` 组织挥空/命中音。
  - 可配置 `comboCountA/comboCountB`，并可开关 heavyA/B、sprintA/B。
  - `AbilitySfxEntry[]` 以 `abilityId` 映射技能音，不再写死 ability1~4 字段。

- **播放控制层**：`Player/CombatSfxController.cs`
  - 监听通用事件后按配置播放音效。
  - 保留原冲突策略：命中音与防御音可通过抑制开关避免叠音。
  - 保留能力3期间 BGM override（Begin/End）。

## 2. 触发来源（生产者）

- `Combat/MeleeFighter.cs`
  - 动画事件 `Whoosh()` -> `CombatSfxSignals.RaiseAttackWhoosh(...)`。

- `Combat/CombatReceiver.cs`
  - 结算命中后 -> `CombatSfxSignals.RaiseHitResolved(...)`。

- `Player/PlayerController.cs`
  - `AbilityImpact()` 生效时 -> `CombatSfxSignals.RaiseAbilityTriggered(...)`（通过通用 `abilityId` 触发）。

- `Player/PlayerAbilitySystem.cs`
  - 技能3开始/结束 -> `RaiseAbility3TimeSlowBegin/End()`。

## 3. 结构收益

- 同一套脚本可覆盖玩家与敌人，配置上按角色差异启用/关闭分组。
- 连段数量可变，避免了固定四段连招硬编码。
- 能力音走 `abilityId` 映射，扩展能力数量时不需要继续加字段。
