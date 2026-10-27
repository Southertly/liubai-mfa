# Windows MFA 内置浏览器评审镜像 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把旧的三方向选择页替换为当前 Windows MFA 正式界面的静态评审镜像，并用 ① 至 ⑦ 支持用户按区域反馈。

**Architecture:** 继续使用现有单文件 HTML 可视化，不引入框架、构建工具或网络依赖。HTML 负责语义结构，CSS 复刻浅色 WinUI 层级并实现响应式布局，少量原生 JavaScript 只负责编号区域的悬停、键盘和选中联动。

**Tech Stack:** HTML5、CSS3、原生 JavaScript、Codex 内置浏览器

---

## 文件结构

- 修改：`C:/Users/liu/.codex/visualizations/2026/08/19/01a01992-9348-7740-b771-ef34ffea2d43/mfa-ui-brainstorm/content/visual-direction.html`
  - 单页包含应用镜像、七个评审区域、编号说明、固定虚构账户数据和本地交互。
- 不修改：`artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest`
  - 这是用户已有改动，必须保持在提交和本次页面工作之外。

### Task 1: 建立静态页面契约并替换旧页面

**Files:**
- Modify: `C:/Users/liu/.codex/visualizations/2026/08/19/01a01992-9348-7740-b771-ef34ffea2d43/mfa-ui-brainstorm/content/visual-direction.html`

- [ ] **Step 1: 运行旧页面契约检查并确认失败**

```powershell
$page = 'C:\Users\liu\.codex\visualizations\2026\08\19\01a01992-9348-7740-b771-ef34ffea2d43\mfa-ui-brainstorm\content\visual-direction.html'
$html = Get-Content -LiteralPath $page -Raw
@('data-zone="1"', 'data-zone="7"', '界面评审镜像', '所有内容均为虚构示例') | ForEach-Object {
    if (-not $html.Contains($_)) { throw "缺少页面契约：$_" }
}
```

预期：命令因缺少 `data-zone="1"` 等新页面标记而失败，证明检查能识别旧页面。

- [ ] **Step 2: 写入完整应用镜像结构**

页面使用以下固定结构和文案，不读取正式应用数据：

```html
<main id="mfa-review" class="review-shell">
  <section class="review-stage" aria-label="Windows MFA 界面评审镜像">
    <p class="sample-notice">界面评审镜像 · 所有内容均为虚构示例</p>
    <article class="app-window">
      <header class="titlebar review-zone" data-zone="1">留白 MFA</header>
      <section class="app-content">
        <header class="page-header review-zone" data-zone="2">验证码 / 本机加密存储 / 导入 / 设置 / 新增</header>
        <div class="search-box review-zone" data-zone="3">搜索账户</div>
        <nav class="group-tabs review-zone" data-zone="4">全部 (6) / 个人 (1)</nav>
        <section class="account-list review-zone" data-zone="5">六张虚构账户卡片</section>
        <div class="expiry-example review-zone" data-zone="6">蓝色进度条与红色临期示例</div>
        <footer class="app-footer review-zone" data-zone="7">验证码每 30 秒自动更新 / 备份与恢复</footer>
      </section>
    </article>
  </section>
  <aside class="review-panel" aria-label="修改区域编号说明">七个可选编号说明</aside>
</main>
```

六张卡片固定使用 `GitHub / demo.github@example.test / 482 731`、`Google / demo.google@example.test / 160 294`、`Meta / demo.meta@example.test / 705 118`、`Microsoft / demo.microsoft@example.test / 319 640`、`Cloudflare / demo.cloudflare@example.test / 846 205`、`Amazon / demo.amazon@example.test / 573 912`。邮箱域名统一为保留测试域 `.test`，页面显著标注“所有内容均为虚构示例”。

- [ ] **Step 3: 实现克制的 WinUI 浅色视觉**

CSS 使用明确的页面变量：`--canvas: #edf3fb`、`--surface: rgba(255,255,255,.72)`、`--card: rgba(255,255,255,.88)`、`--ink: #172033`、`--muted: #657083`、`--accent: #356fd6`、`--critical: #c93b48`。应用镜像宽度约 `560px`，账户卡片使用 `16px` 圆角和细边框，验证码使用等宽字体，复制按钮与进度条位置匹配正式界面。编号标记使用圆形蓝色徽标，不覆盖关键内容；第 6 区同时展示正常蓝色和临期红色状态。

- [ ] **Step 4: 实现编号联动和响应式布局**

```javascript
const zones = [...document.querySelectorAll('[data-zone]')];
const setActive = (id) => {
  zones.forEach((node) => node.classList.toggle('is-active', node.dataset.zone === id));
};
zones.forEach((node) => {
  node.addEventListener('mouseenter', () => setActive(node.dataset.zone));
  node.addEventListener('focusin', () => setActive(node.dataset.zone));
  node.addEventListener('click', () => setActive(node.dataset.zone));
});
```

说明面板的七个按钮与镜像区域共享 `data-zone`。桌面宽度使用两列，窄于 `880px` 时改为上下排列；尊重 `prefers-reduced-motion`，关闭非必要过渡。

- [ ] **Step 5: 运行页面契约检查并确认通过**

```powershell
$page = 'C:\Users\liu\.codex\visualizations\2026\08\19\01a01992-9348-7740-b771-ef34ffea2d43\mfa-ui-brainstorm\content\visual-direction.html'
$html = Get-Content -LiteralPath $page -Raw
@('data-zone="1"', 'data-zone="7"', '界面评审镜像', '所有内容均为虚构示例', 'demo.github@example.test', 'prefers-reduced-motion') | ForEach-Object {
    if (-not $html.Contains($_)) { throw "缺少页面契约：$_" }
}
if ($html -match 'liu@example\.com|work@example\.com|ops@example\.com') { throw '检测到旧示例账户' }
"PASS: 页面契约与虚构数据边界已满足"
```

预期：输出 `PASS: 页面契约与虚构数据边界已满足`。

### Task 2: 内置浏览器视觉验收与交付

**Files:**
- Verify: `C:/Users/liu/.codex/visualizations/2026/08/19/01a01992-9348-7740-b771-ef34ffea2d43/mfa-ui-brainstorm/content/visual-direction.html`

- [ ] **Step 1: 刷新现有内置浏览器页面**

在已绑定的内置浏览器标签页打开 `http://localhost:57111/`，等待页面稳定，确认页面标题或可见文本包含“界面评审镜像”。

- [ ] **Step 2: 检查页面错误与关键可见内容**

读取页面快照和控制台错误，确认 `①` 至 `⑦`、六张账户卡片、虚构示例提示、蓝色正常进度条与红色临期状态均可见，且没有脚本异常。

- [ ] **Step 3: 检查交互与窄宽度布局**

点击编号 `⑤`，确认账户卡片区与说明项同时高亮；临时缩窄视口，确认说明面板移到应用镜像下方且没有横向溢出，然后恢复桌面视口。

- [ ] **Step 4: 检查仓库边界并交付**

```powershell
git status --short
git diff --cached --name-only
```

预期：`Package.appxmanifest` 仍只是用户未提交改动，本次浏览器页面不进入 Windows 产品提交。保持内置浏览器可见并告诉用户可直接按“编号 + 修改意见”反馈。
