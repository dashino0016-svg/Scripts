# Enemy vs Player 下坡高速触发坠落差异分析

## 结论（TL;DR）
敌人并不是因为“绝对速度更快”才更容易进坠落，而是因为 **敌人移动实现对“短暂离地”容错更低**：

1. 敌人一旦出现 `wasGrounded && !isGrounded` 就立即打 `EnterFall` Trigger。  
2. 敌人地面段虽然做了坡面投影，但随后又用 `motion.y = velocityY` 覆盖了投影产生的坡向 Y 分量。  
3. 敌人没有像玩家那样在“非跳跃离地”时保留空中水平惯性（`airHorizontalVelocity`），所以在下坡边缘更容易出现接地抖动。  

玩家脚本里正好针对上述情况做了补偿（源码注释也明确写了“关键修复”），因此即便速度更高也更不容易误触发坠落状态。

---

## 关键差异 1：坠落状态触发条件更“激进”
- 敌人：在 `Update` 中，只要 `wasGrounded && !isGrounded`，就立刻 `anim.SetTrigger(AnimEnterFall)`。这会把任何短暂离地（如下坡接缝、台阶边缘）都当成“进入坠落”。
- 玩家：没有 `EnterFall` trigger 这条路径，主要通过 `IsGrounded` + `VerticalVelocity` 驱动空中表现，短暂离地通常不会被一次性 trigger 放大。

这解释了“敌人经常突然进坠落动画，而玩家更平滑”。

## 关键差异 2：敌人把坡面投影的 Y 分量丢掉了
- 玩家移动：
  - 先 `horizontal = Vector3.ProjectOnPlane(...)`（带坡向分量）；
  - 最终 `motion += Vector3.up * velocityY`（叠加重力，不覆盖 `horizontal` 原有 Y）。
- 敌人移动：
  - 也会 `horizontal = Vector3.ProjectOnPlane(...)`；
  - 但随后 `motion.y = velocityY`，直接覆盖掉 `horizontal` 的 Y。

结果：敌人在坡面上“贴坡”连续性更差，下坡高速时更容易出现轻微离地，进而触发前面的 `EnterFall`。

## 关键差异 3：玩家有离地惯性缓冲，敌人没有
- 玩家在地面阶段会在合适条件下缓存 `airHorizontalVelocity`，并在非地面阶段继续使用/平滑该速度方向。
- 敌人没有这套“非跳跃离地”的惯性延续，离地后水平运动更容易断裂，落到地面检测边界时抖动更明显。

---

## 为什么“敌人跑得更慢反而更容易坠落”
脚本默认值确实是：玩家 `runSpeed=3`、敌人 `runSpeed=2`。  
但问题核心是 **运动解算稳定性差异**，不是标称速度大小：

- 敌人对短暂离地的处理是“立刻进坠落 trigger”；
- 玩家有贴坡 Y 保留 + 空中惯性缓冲。

因此即使敌人更慢，也会比玩家更常“误入坠落状态”。

---

## 可优先尝试的修复方向（按收益排序）
1. **先修 EnemyMove 的 Y 覆盖**：把 `motion.y = velocityY` 改为与玩家同思路的叠加方式（保留投影 Y）。
2. **给敌人加短暂离地缓冲策略**：复用玩家 `airHorizontalVelocity` 方案，至少在“非跳跃离地”时保持水平惯性。
3. **降低 EnterFall 触发敏感度**：例如要求连续离地若干帧或 `velocityY` 低于阈值再触发。
4. **再做参数微调**：`groundCheckDistance/Offset/Radius`、`groundedGraceTime` 只做精调，避免只靠参数掩盖逻辑问题。
