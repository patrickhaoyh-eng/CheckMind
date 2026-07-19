# Define notch profiles 门禁决策与回归清单

## 1. 状态
- 状态：Accepted
- 日期：2026-07-19
- 适用范围：`Sine Setup -> Define notch profiles -> Notch Profile` 业务链路

## 2. 背景
- `Define notch profiles` 曾引入第二刀门禁，初衷是在进入 `Notch Profile` 子窗口前，先阻断“点位仍合法但页面布局已漂移”的风险。
- 初版方案使用 `notch_profiles_layout` 的 ROI 哈希作为硬门禁。
- 真实业务扩展到第 3 条内容后，暴露出左侧列表条目数量与名称是动态业务内容，不具备稳定哈希前提。
- 继续依赖左侧列表内容做硬签名，会把“业务数据变化”误判为“页面结构漂移”，导致正式链路在进入第 1 条之前就被提前阻断。

## 3. 决策
- 第二刀门禁的正式口径调整为：`几何/流程门禁放行 + 哈希漂移留证不阻断`。
- `notch_profiles_layout` 的 ROI 仅保留底部控制区等相对稳定的页面 chrome，不再依赖左侧动态列表内容。
- 运行时若哈希漂移：
  - 保留 `define_notch_profiles_layout_hash_warning.json`
  - 保留截图证据
  - 写出 `runner.notch_profiles_layout_hash_mismatch_ignored`
  - 不阻断后续 `Notch Profile` 扫描
- 运行时若 ROI 可采集且页面流程可继续：
  - 写出 `runner.notch_profiles_layout_geometry_verified`
  - 允许后续 `row=N` 的正式业务扫描继续执行

## 4. 不采用的方案
- 继续扩大或重标左侧列表 ROI：
  - 拒绝原因：列表数量、名称、选中态、滚动条状态都随业务变化，不具备长期稳定性。
- 保留“内容级硬签名”，但每次模板变化就重标：
  - 拒绝原因：这会把动态业务数据错误地纳入静态基线，实际使用成本过高且必然频繁误拦。
- 完全删除第二刀门禁：
  - 拒绝原因：会丢失对 `Define notch profiles` 页面在位与 ROI 可采集性的前置保护。

## 5. 长期保留的运行证据
- 保留 `runner.notch_profiles_layout_hash_mismatch_ignored`
  - 价值：记录门禁已观察到哈希漂移，但按当前决策不阻断，可用于后续审计页面稳定性。
- 保留 `runner.notch_profiles_layout_geometry_verified`
  - 价值：证明第二刀门禁已通过页面在位/ROI 可采集/流程可继续的最小条件校验。
- 删除 `runner.notch_profiles_layout_signature_probe`
  - 原因：该 marker 仅服务于本轮对比 `pixel hash` 与 `png bytes hash` 的专项定位，长期保留会增加日志噪声，日常回归价值低。

## 6. 已形成的正式基线
- 模板口径成功：`notchProfileCount=3`
- 模板口径 mismatch：`notchProfileCount=4`
- 显式索引成功：`NotchProfileIndexes="1,2,3"`
- 显式索引 mismatch：`NotchProfileIndexes="1,2,3,4"`

## 7. 端到端回归建议清单
- 成功路径回归
  - 运行 `-NotchProfileCount 3`
  - 期望 `results.json.status=completed`
  - 期望 `row=1/2/3` 都有 `runner.notch_profile_scan_completed`
- mismatch 路径回归
  - 运行 `-NotchProfileCount 4`
  - 期望 `results.json.status=completed_with_warning`
  - 期望 `resultCode=NOTCH_PROFILE_COUNT_MISMATCH`
  - 期望 `failedRow=4`
- 显式索引成功回归
  - 运行 `-NotchProfileIndexes "1,2,3"`
  - 期望 `alerts=[]`
  - 期望 `NotchProfileCountMismatch=null`
- 显式索引 mismatch 回归
  - 运行 `-NotchProfileIndexes "1,2,3,4"`
  - 期望终端输出 `notch_profile_count_mismatch=requested:4;completed:3;failedRow:4`
  - 期望 `message` 使用 `Notch Profile Indexes` 口径
- 门禁证据回归
  - 期望至少出现 `runner.notch_profiles_layout_geometry_verified`
  - 若出现哈希漂移，期望生成 `define_notch_profiles_layout_hash_warning.json`
  - 不接受“仅因哈希漂移而阻断正式业务链路”

## 8. 触发重新审视本决策的条件
- `Define notch profiles` 的底部控制区自身出现明显布局漂移，导致 `geometry_verified` 不再可信。
- 后续发现页面在位但按钮区状态不足以证明可执行，需要补充新的稳定几何特征。
- 出现新的误放行证据，证明当前“几何/流程门禁”不足以覆盖真实风险。
