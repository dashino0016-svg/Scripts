# 复活点闭环当前实现状态审查

## 已实现（较完整）
1. **单存档记录**：`SavePointManager` 在 `BeginSaveFlow` 时把当前点写入 `lastSavePoint`（单槽位）。
2. **进入存档流程的交互门槛**：`PlayerSavePointInteractor` 已限制“按键触发 + 在触发区 + 非暂停 + 非持剑 + 非动作锁”。
3. **进入存档时玩家处理**：对齐锚点、加流程锁、无敌、播 Save 动画。
4. **Save 动画结束后黑屏进入 UI**：`NotifySaveAnimEnd` 通过 `ScreenFader.FadeOutIn` 在黑屏中打开升级 UI。
5. **升级 UI 暂停世界与 UI BGM**：`UpgradeUIManager.OpenImmediate` 会暂停时间并切换到 UI 专用循环 BGM；关闭时恢复。
6. **退出 UI 的黑屏 + Exit 动画链路**：`OnUIExitRequested` 中黑屏阶段关闭 UI、补满玩家 HP、保持无敌和锁，再触发 Exit。
7. **死亡回溯基本框架**：玩家死亡后延迟黑屏，在黑屏期间对齐复活锚点；有存档点时进入 Exit 姿态，否则回 Idle。
8. **玩家复活恢复规则（当前实现）**：补满 HP，不改体力/能量（special），符合“HP 满、能量不变”的方向。

## 已有能力但尚未接入主流程
1. `EnemyController.ResetToHomeForCheckpoint(Transform homePoint)` 已实现了“重置敌人状态 + 满血满体力 + 回 HomePoint + 回 notcombat”的核心逻辑。
2. `EnemyState.ForceResetToNotCombatForCheckpoint()` 也提供了从 Dead/Combat 等强制回归 NotCombat 的状态复位能力。

## 当前主要缺口/问题
1. **敌人刷新未接线（最关键）**
   - 在 `SavePointManager` 的 UI 进入、UI 退出、死亡回溯黑屏中，未调用任何“遍历全部敌人并执行 checkpoint reset”的逻辑。
   - 结果：目标需求里“黑屏期间所有敌人（含死亡敌人）回 HomePoint 并恢复”的核心点尚未落地。

2. **死亡敌人复活链路虽然有局部实现，但缺统一调度入口**
   - `EnemyController.ResetToHomeForCheckpoint` 可处理死亡后复位，但没有被 `SavePointManager` 在黑屏中触发。

3. **升级系统功能仍是占位**
   - `OnUnlockDroneBurstRequested` 与 `UpgradeUIManager.OnClickUnlockDroneBurst` 目前仅日志占位，升级兑现逻辑未实现。

4. **存档点流程触发条件与“非持剑状态”一致，但缺少更明确的提示/失败反馈**
   - 目前按键失败是静默 return，调试和体验上缺“为何不能交互”的可视提示。

5. **`currentSavePoint` 字段当前仅赋值未用于后续流程判断**
   - 不影响主功能，但存在冗余状态，容易让后续维护者误判其用途。

## 阶段判断（按你的总目标拆分）
- **阶段 A（复活点交互 + 单存档）**：基本完成。
- **阶段 B（黑屏进升级 UI + 退出链路 + 玩家恢复）**：基本完成，升级效果本体未完成。
- **阶段 C（敌人全量黑屏刷新，含死亡敌人）**：关键未完成（已有底层函数但未接主流程）。
- **阶段 D（玩家死亡后回 lastSavePoint 并复用退出姿态）**：主链路已完成，但缺“同步敌人刷新”这一关键步骤。

## 建议下一步（按优先级）
1. 在 `SavePointManager` 增加 `RefreshAllEnemiesForCheckpoint()`：
   - `FindObjectsByType<EnemyController>(FindObjectsInactive.Include, ...)`
   - 读取各敌人的 `LostTarget.homePoint`
   - 逐个调用 `ResetToHomeForCheckpoint(homePoint)`
2. 在三个黑屏 `midAction` 接入它：
   - `NotifySaveAnimEnd`（进入 UI 时）
   - `OnUIExitRequested`（退出 UI 时）
   - `ExecuteDeathRespawnDuringBlack`（死亡回溯时）
3. 确认“能量值”字段映射（当前更像 `CurrentSpecial`），在 UI/文档里统一术语，避免以后把 stamina 当能量误补。
4. 补齐升级按钮实际解锁逻辑，并与存档点 UI 生命周期绑定持久化。
