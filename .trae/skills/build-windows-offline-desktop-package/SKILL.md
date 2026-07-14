---
name: "build-windows-offline-desktop-package"
description: "Builds offline Windows desktop packages with app/data separation, runtime staging, assembly, and installer outputs. Invoke when preparing a full installer or release candidate for a local desktop app."
---

# Build Windows Offline Desktop Package

这个 Skill 用于把一个“本地桌面壳 + 前后端 + 本地运行时”的项目整理成可离线交付的 Windows 安装产物。它不是泛泛地“做打包”，而是强调：

- `app/` / `data/` 分层
- 离线运行时装配
- 桌面壳、本地前端、后端的统一交付
- 全量安装包与校验产物输出

## 何时调用

在以下场景调用：

- 用户要求输出 Windows 离线安装包
- 用户要求从开发源码生成可部署的桌面交付物
- 用户要求重新 assemble 打包工作区并构建安装器
- 用户要求做发布候选包、交付包、验收安装包

不适合以下场景：

- 只改某个前端页面
- 只修一个后端接口
- 只需要前端补丁包而不需要全量安装包

## 核心目标

1. 确认源码输入和打包工作区边界
2. 组装 `package-root/app`
3. 装配离线运行时
4. 生成前端静态构建产物
5. 归档 launcher 和桌面壳交付输入
6. 构建全量安装包
7. 输出产物路径、版本、校验文件和已知限制

## 必须遵守的约束

- 默认遵守 `windows-offline-app-layout`
- 默认遵守 `webview2-local-routing-and-assets`
- 默认遵守 `installer-stop-stack-before-upgrade`
- 不在主源码目录里做打包污染
- 运行时、安装器、临时构建目录必须与主源码隔离
- 不能假设目标机已安装 Python、Node 或开发依赖

## 输入信息

调用前尽量确认：

- 主源码目录
- 打包工作区目录
- 当前版本号
- 是否需要全量安装包
- 安装器输出目录
- NSIS 可执行路径
- 是否已有 Python / WebView2 离线资源

如果缺失这些信息，必须先补齐，再执行。

## 推荐执行步骤

### 第一步：确认输入边界
- 找到主源码目录
- 找到打包工作区
- 明确哪些文件来自源码仓，哪些属于打包侧热修
- 检查是否已存在旧版产物和历史缓存

### 第二步：准备打包输入
- 同步源码到打包工作区
- 清理无关临时目录
- 校验图标、runtime、安装器脚本、桌面壳源码是否齐备

### 第三步：组装应用层
- 重建 `package-root/app`
- 组装后端代码
- 构建并复制前端产物
- 组装 launcher、runtime、desktop 交付层

### 第四步：构建安装器
- 生成全量安装包
- 输出 IT 友好别名
- 生成 `sha256`

### 第五步：整理交付结果
- 明确产物路径
- 明确版本号
- 明确本次打包输入来源
- 给出最小验收建议

## 标准输出

完成后必须给出：

```markdown
## 输入来源
- 源码目录：
- 打包目录：
- 版本：

## 安装包产物
- 全量包：
- 别名包：
- sha256：

## 已完成步骤
- assemble：
- runtime：
- frontend build：
- installer build：

## 已知限制
- 

## 建议验收
- 
```

## 失败处理

若过程中失败，优先定位以下问题：

1. 运行时资源缺失
2. 前端构建失败
3. 桌面壳发布失败
4. 安装器语法错误
5. 历史缓存或旧目录清理卡住

失败时不要只报告“build failed”，应明确：

- 失败阶段
- 失败命令
- 影响范围
- 是否已生成部分产物
- 推荐修复方向

## 示例

### 示例 1：完整发布
- 用户说：“帮我把这个桌面项目做成离线安装包”
- 你应调用本 Skill

### 示例 2：发布候选验证
- 用户说：“基于当前源码重建一版 setup.exe 并给我产物路径”
- 你应调用本 Skill

## 成功标准

- `package-root/app` 结构完整
- 运行时资源装配完成
- 前端可离线加载
- 全量安装包生成成功
- 产物路径和校验信息明确
