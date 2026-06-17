# Third-Party Notices

Irozukume は以下の第三者ソフトウェアを利用しています。各ソフトウェアのライセンスと著作権表示をここに集約します。MIT ライセンスのコンポーネントが複数あるため、MIT License 本文は末尾の「MIT License 全文」に一度だけ掲載し、各項目からはそれを参照します。

---

## 1. Windows App SDK (Microsoft.WindowsAppSDK)

WinUI 3 のランタイムおよびフレームワーク。本アプリの UI 基盤として全面的に利用しており、self-contained 配置で関連 DLL を同梱しています。

- バージョン: 1.8.260508005
- 著作権者: Microsoft Corporation
- ソース: https://github.com/microsoft/WindowsAppSDK
- ライセンス: Microsoft Software License Terms (Microsoft Windows App SDK)
- ライセンス本文: 同梱の [THIRD-PARTY-NOTICES-WINDOWSAPPSDK.TXT](THIRD-PARTY-NOTICES-WINDOWSAPPSDK.TXT) を参照

NuGet パッケージによってアプリへ配置 (binplace) されるファイルは、同ライセンスの DISTRIBUTABLE CODE 条項により再配布が許諾されています。

---

## 2. .NET 10 Runtime (Self-Contained 同梱)

CoreCLR・基本クラスライブラリ・Windows Desktop ランタイムを self-contained 配置で同梱しています。

- 著作権者: .NET Foundation and Contributors
- ソース: https://github.com/dotnet/runtime
- ライセンス: MIT License — 本文は末尾参照

ランタイム内部には Unicode・ICU・zlib 等の第三者コンポーネントを含みます。それらの著作権・許諾表示は、同梱の [THIRD-PARTY-NOTICES-DOTNET.TXT](THIRD-PARTY-NOTICES-DOTNET.TXT) (dotnet/runtime の THIRD-PARTY-NOTICES.TXT のコピー) を参照してください。

---

## 3. Windows Community Toolkit

設定画面のカード型レイアウト (SettingsCard / SettingsExpander) と、メインウィンドウのペイン分割 (GridSplitter 等) に利用しています。CommunityToolkit.WinUI.Controls.SettingsControls と CommunityToolkit.WinUI.Controls.Sizers を含みます。

- バージョン: 8.2.251219
- 著作権者: .NET Foundation and Contributors
- ソース: https://github.com/CommunityToolkit/Windows
- ライセンス: MIT License — 本文は末尾参照

---

## 4. H.NotifyIcon.WinUI

WinUI 3 用のシステムトレイアイコン (NotifyIcon) ライブラリ。トレイ常駐 UI に利用しています。共有コンポーネントの H.NotifyIcon を含みます。

- バージョン: 2.4.2-dev.17
- 著者: HavenDV
- ソース: https://github.com/HavenDV/H.NotifyIcon
- ライセンス: MIT License (Copyright (c) 2020 havendv) — 本文は末尾参照

---

## 5. Microsoft.Graphics.Win2D

GPU による 2D 描画 API。色のグラデーション描画や、画面カラーピッカーのルーペ (グラス) 効果の描画に利用しています。

- バージョン: 1.4.0
- 著作権者: Microsoft Corporation
- ソース: https://github.com/microsoft/Win2D
- ライセンス: MIT License (Copyright (c) Microsoft Corporation) — 本文は末尾参照

NuGet パッケージのメタデータには旧来の Microsoft EULA URL が残っていますが、ソースリポジトリの LICENSE.txt は MIT License です。

---

## MIT License 全文

上記のうち MIT License と記載したコンポーネント (.NET 10 Runtime・Windows Community Toolkit・H.NotifyIcon.WinUI・Microsoft.Graphics.Win2D) に共通して適用されるライセンス本文です。著作権表示は各項目に記載のものを参照してください。

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
