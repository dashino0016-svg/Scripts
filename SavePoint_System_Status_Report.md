# SavePoint / Respawn / UpgradeUI 当前实现进度评估

## 结论（TL;DR）
项目已经完成了**主流程骨架**，可跑通：
- 靠近复活点 + 按键触发存档点流程
- 进入存档动作期间锁操作 + 无敌
- 动作结束黑屏转场并打开升级 UI（暂停世界 + UI BGM 覆盖）
- 退出 UI 黑屏后播放退出动作
- 玩家死亡后延迟黑屏回溯到最近复活点（或初始点）

但距离目标“绝地武士式闭环”仍有关键缺口，主要集中在：
1. 敌人“包括死亡敌人”统一刷新未达标（死亡敌人当前不会复位）。
2. 敌人刷新后“补满血+体力”未实现（只做了位置/状态重置）。
3. 退出存档后“玩家能量保持不变”目前是被动满足，未做显式约束与回归保护。
4. 动画事件驱动强依赖（SaveEnd/ExitEnd）；若动画事件漏配，流程会卡死在中间状态。

---

## 分目标完成度

## 1) 场景多个复活点 + 非持剑靠近互动触发
**完成度：高（约 90%）**

- `SavePoint` 提供交互锚点和重生锚点（可回退到 `interactAnchor`）。
- `PlayerSavePointInteractor` 在按键、可交互范围、未暂停、未持剑、非动作锁条件下触发 `BeginSaveFlow`。
- 目前交互逻辑是“进入触发器即记录当前 SavePoint”，支持多复活点。

**潜在问题**
- 未看到 UI 提示（如“按 E 休息”）控制逻辑，交互反馈可能不足。

## 2) 进入存档流程：对齐锚点、存档动作、无敌、禁操作
**完成度：高（约 90%）**

- `BeginSaveFlow` 对齐玩家到 `interactAnchor`，开启 `SetCheckpointFlowLock(true)`，`ForceSetInvincible(true)`，触发 `Save` 动画。
- Player 端 `SetCheckpointFlowLock` 会清空战斗/翻滚/格挡等锁状态，基本符合“禁止所有操作”。

**潜在问题**
- 如果 `Save` 动画未正确派发 `Checkpoint_SaveEnd`（或 `SavePointManager.NotifySaveAnimEnd`），流程不会进 UI。

## 3) 存档动作结束后黑屏转场进入升级 UI（暂停世界+UI音频）
**完成度：高（约 85%）**

- `NotifySaveAnimEnd` 使用 `ScreenFader.FadeOutIn`，黑屏中 `OpenImmediate()`。
- `UpgradeUIManager.OpenImmediate` 暂停时间（`TimeController` 或 `Time.timeScale=0`），切换 BGM 覆盖循环。

**潜在问题**
- “只播放 UI 专属 BGM 与 UI 音效”在代码层主要通过 BGM 覆盖实现，其他 3D 音频是否被总线静音未见统一处理。

## 4) 退出升级 UI：黑屏转场→退出动作→非持剑 Idle，玩家 HP 满、能量不变
**完成度：中高（约 75%）**

- `OnUIExitRequested`：黑屏中关闭 UI + 触发 `Exit` 动画。
- `NotifyExitAnimEnd`：调用 `ApplyRespawnRecovery()`，其中会：
  - 复位动作/受击锁
  - 解除无敌
  - 通过 checkpoint lock 脉冲清玩家锁
  - `ReviveFullHP()`（HP 满）
  - 视觉层面 `IsArmed=false`、武器挂腰

**潜在问题**
- “能量值不变”：当前 `CombatStats.ReviveFullHP()` 仅改 HP，不改 Special；这是**间接符合**。但没有显式保存/恢复能量值，未来改动可能破坏需求。
- 同样依赖 `Exit` 动画事件 `Checkpoint_ExitEnd`，漏配会卡在 `ExitingAnim`。

## 5) 敌人在黑屏期间刷新到 HomePoint，补满血体力，进入 notcombat 巡逻
**完成度：中（约 55%）**

已做：
- `RefreshEnemiesDuringBlackScreen` 遍历敌人并调用 `enemy.ResetToHomeForCheckpoint(homePoint)`。
- `ResetToHomeForCheckpoint` 会清目标/仇恨、停战斗、传送 HomePoint、收刀、重置若干组件，最后 `enemyState.OnReturnHomeReached()`，能自然回到 NotCombat。

未做/有偏差：
- **死亡敌人不刷新**：`ResetToHomeForCheckpoint` 一开始就 `if (enemyState.Current == Dead) return;`，与“包括死亡敌人”冲突。
- **未补满敌人 HP/体力**：未调用类似 `ReviveFullHP()` / stamina full 恢复。
- 对被禁用对象/死亡后脚本禁用对象的覆盖能力取决于 `Resources.FindObjectsOfTypeAll<EnemyController>()` + 组件状态，存在边界风险。

## 6) 单存档：只记录最近一次进入升级 UI 的复活点
**完成度：中（约 70%）**

- 当前 `BeginSaveFlow` 里 `lastSavePoint = savePoint`，确实是单槽覆盖。
- 但记录时机是“开始存档流程”而不是“进入升级 UI 成功后”。

**偏差**
- 需求写的是“最近一次进入升级UI的复活点”，建议把写入时机延后到 `NotifySaveAnimEnd` 的黑屏中（UI 实际打开时）更严谨。

## 7) 玩家死亡回溯：延迟黑屏→到 lastSavePoint→退出动作→非持剑Idle；HP 满、能量不变；敌人同样黑屏刷新
**完成度：中高（约 80%）**

- `OnPlayerDead` -> `CoDeathRespawnEntry` 延迟后黑屏。
- 黑屏中 `ExecuteDeathRespawnDuringBlack`：
  - 对齐到 respawn anchor（lastSavePoint 或 initialSavePoint）
  - 刷新敌人
  - 有存档点则走“退出动作重生”，否则直接 Idle 重生
- 对有存档点情况：`PrepareCheckpointExitRespawnInBlack` 会无敌+HP满+锁流程并切到 Exit 动画，之后 `NotifyExitAnimEnd` 再回 Idle 并解除锁。

**潜在问题**
- 同样受敌人刷新缺口影响（死亡敌人、体力血量重置）。
- 若 `exitStateName` 状态名配置错误，仅触发 trigger 兜底，依赖 Animator 图配置质量。

---

## 优先级问题清单（建议先修）

### P0（必须修）
1. 敌人刷新覆盖“死亡敌人”：去掉 dead early-return，或新增强制复活路径。  
2. 敌人刷新时补满 HP 与体力：扩展 `CombatStats`（例如 `ReviveFullHPAndStamina()`）并在 checkpoint refresh 调用。  
3. 把 `lastSavePoint` 写入时机从 `BeginSaveFlow` 改为“UI 打开成功点”（黑屏中/`OpenImmediate` 后）。

### P1（强烈建议）
4. 给 Save/Exit 流程增加超时兜底，防动画事件漏配导致状态卡死。  
5. 对“能量不变”加显式保护（进入流程前缓存 special，恢复时写回，或加断言日志）。  
6. 增加敌人刷新日志统计（刷新总数、死亡复活数、无 HomePoint 数），便于联调。

### P2（体验优化）
7. 交互提示 UI（可互动高亮、按键提示）。  
8. UI 模式下音频总线策略：除 UI/BGM 以外是否静音，做统一 Mixer Snapshot。

---

## 当前里程碑判断
你现在大约在**“功能串联可演示（Vertical Slice）”阶段**：主链路已经打通，但还没达到“可上线规则完整性”。

若按开发阶段划分：
- ✅ 流程原型（Prototype）
- ✅ 系统联通（Integration）
- ⚠️ 规则闭环（Polish & Rule-Complete）——还差敌人刷新与单存档时机等关键一致性
