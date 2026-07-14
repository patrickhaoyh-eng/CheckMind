---
name: "create-nsis-frontend-patch-installer"
description: "Creates an NSIS frontend-only patch installer that upgrades an existing local desktop app safely. Invoke when shipping a UI/web-layer hotfix without replacing the full app."
---

# Create NSIS Frontend Patch Installer

这个 Skill 用于生成“仅覆盖前端层”的 NSIS 增量更新包，适用于本地桌面项目中：

- 后端无需变更
- 运行时无需重装
- 桌面壳无需重发
- 只需要替换 `app/frontend`

它的核心目标不是“包尽量小”本身，而是：

- 以最小覆盖范围交付 UI / Web 层热修
- 保留 `data/`
- 升级前安全停栈
- 给已安装电脑提供低风险补丁路径

## 何时调用

在以下场景调用：

- 用户要求做前端补丁包
- 用户要求只覆盖前端目录
- 用户要求做 NSIS 增量升级包
- 用户要求给已安装机器发 UI 热修

不适合以下场景：

- 后端接口或数据库结构也发生变化
- 运行时、桌面壳、launcher 也必须同步更新
- 需要首装能力，而不是升级能力

## 核心原则

1. 只覆盖最小必要目录
2. 默认仅覆盖 `app/frontend`
3. 升级前必须先停桌面壳和后台
4. 必须自动识别历史安装目录
5. 必须保留 `data/`
6. 必须备份旧前端目录

## 输入信息

调用前尽量确认：

- 现有安装器脚本或 NSIS 模板
- 打包工作区目录
- `package-root/app/frontend` 是否已准备好
- 当前版本号
- 安装器输出目录
- 是否需要别名文件名

## 推荐执行步骤

### 第一步：确认补丁边界
- 明确本次是否真的只改了前端
- 如果后端、launcher、runtime、desktop 有变化，不应继续使用此 Skill

### 第二步：确认安装目标识别策略
- 优先读取历史 `InstallLocation`
- 若无法识别但目标目录看起来像已安装目录，应要求显式确认
- 不能默认把补丁发成“也支持首装”

### 第三步：设计升级前停栈
- 先停桌面壳
- 再调用正式停止脚本
- 停机失败不得静默继续

### 第四步：设计最小备份
- 备份旧版 `app/frontend`
- 备份目录应带时间戳
- 备份行为必须写入安装日志

### 第五步：构建补丁包
- 仅删除/覆盖 `app/frontend`
- 更新必要版本标记
- 输出补丁包和 `sha256`

## 推荐输出

完成后必须给出：

```markdown
## 补丁范围
- 覆盖目录：
- 保留目录：

## 升级前行为
- 安装目录识别：
- 停桌面壳：
- 停后台：

## 备份策略
- 备份目录：
- 时间戳：

## 输出产物
- patch installer：
- alias：
- sha256：
```

## 必须遵守的约束

- 默认遵守 `windows-offline-app-layout`
- 默认遵守 `installer-stop-stack-before-upgrade`
- 默认不触碰 `data/`
- 默认不把补丁包设计成“兼容首装”
- 默认不覆盖 `app/backend`、`app/runtime`、`app/desktop`

## 明确禁止

- 把前端补丁包做成隐式全量覆盖
- 在未停后台的情况下直接覆盖 `app/frontend`
- 不做任何备份就删除旧前端
- 无法识别安装目录时仍继续默认覆盖
- 后端有变更却假装前端补丁足够

## 示例

### 示例 1：UI 热修
- 用户说：“已安装电脑只需要升级前端页面，后端不动”
- 你应调用本 Skill

### 示例 2：快速补丁
- 用户说：“帮我做一个只覆盖 app/frontend 的 NSIS 补丁包”
- 你应调用本 Skill

## 不该调用的示例

- 用户说：“后端也改了，顺便一起更新”
- 这不应使用本 Skill，应改走全量安装包或更完整的升级链路

## 成功标准

- 补丁包只覆盖 `app/frontend`
- 旧安装目录可被识别
- 升级前完成停栈
- 旧前端被备份
- `data/` 保持不动
- 补丁包与校验文件生成成功
