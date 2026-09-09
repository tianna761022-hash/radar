---
title: 台股即將起漲雷達
emoji: 🚀
colorFrom: red
colorTo: indigo
sdk: docker
app_port: 5000
pinned: false
---

# 🚀 台股全自動純多頭紅色波段飆股雷達 - 雲端免費部署指南

本專案已完整配置雲端 Docker 容器與跨平台支援，您可以將其免費部署在雲端平台（如 **Render.com** 或 **Hugging Face Spaces**），取得專屬網址後即可在**手機 (iOS/Android)、平板、家中電腦或隨處**免安裝隨時查看！

---

## 🌟 最推薦：Render.com 免費部署（3 步驟，完全免費）

[Render.com](https://render.com) 提供永久免費的 Web Service 方案，支援自動 HTTPS 安全網址。

### 步驟 1：將程式碼上傳至您的 GitHub
1. 前往 [GitHub.com](https://github.com) 登入或免費註冊。
2. 點擊右上角 **「+」 $\rightarrow$ 「New repository」**。
3. 取名（例如：`taiwan-stock-radar`），選擇 **Public（公開）** 或 **Private（私人）**，點擊 **Create repository**。
4. 將本資料夾 (`d:\stk`) 內的所有檔案上傳到該 GitHub 倉庫中。

---

### 步驟 2：在 Render 建立免費雲端服務
1. 前往 [Render.com](https://render.com) 點擊 **Get Started**（直接點「Continue with GitHub」登入）。
2. 在後台點擊右上角 **「New +」 $\rightarrow$ 選擇「Web Service」**。
3. 選擇 **「Build and deploy from a Git repository」** $\rightarrow$ 點選您剛才建立的 GitHub 倉庫（如 `taiwan-stock-radar`）。
4. 在設定頁面：
   - **Name**：輸入自訂名稱（例如：`my-stock-radar`）
   - **Region**：選擇 `Singapore`（新加坡，距離台灣最近速度最快）
   - **Language / Environment**：選擇 **`Docker`**（系統會自動讀取專案內的 `Dockerfile`）
   - **Instance Type**：選擇 **`Free`**（$0/月 免費方案）
5. 點擊最下方 **「Create Web Service」**！

---

### 步驟 3：享受專屬雲端看盤網址！
- Render 會在 2~3 分鐘內自動編譯並啟動您的股票雷達系統。
- 完成後，畫面左上方會產生您的**專屬專屬 HTTPS 網址**（例如：`https://my-stock-radar.onrender.com`）。
- **無論您人在哪裡、用 iPhone、Android 手機、iPad 或任何電腦瀏覽器，直接打開這個網址就能隨時隨地查看每日即將起漲的股票與分析報告！**

---

## 💡 備選方案 2：Hugging Face Spaces 免費部署（永久不休眠）

1. 前往 [Hugging Face](https://huggingface.co) 免費註冊登入。
2. 點擊右上角個人頭像 $\rightarrow$ **「New Space」**。
3. 設定：
   - **Space Name**：例如 `stock-radar`
   - **Space SDK**：選擇 **`Docker`** $\rightarrow$ 選擇 `Blank`
   - **Space Hardware**：選擇 **`CPU Basic (Free, 2 vCPU, 16GB RAM)`**
4. 點擊 **Create Space**。
5. 在「Files and versions」頁面，將 `d:\stk` 中的所有檔案拖曳上傳並點擊 Commit。
6. 系統會自動啟動，直接給您一個永久免費的看盤網址！
