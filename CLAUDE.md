# 公文書くろぬり (kuronuri)

Windows 向け PDF 黒塗り（墨消し）専用デスクトップアプリ。自治体の LGWAN 端末（完全オフライン・管理者権限なし・GPU なし低スペック）で動かす。MIT ライセンスで公開予定。

**設計の正は `docs/ARCHITECTURE.md`、作業順は `docs/TASKS.md`。** 実装前に必ず該当節を読むこと。設計と違う実装をしたくなったら、先に ARCHITECTURE.md を直してから実装する。

**`docs/REFERENCE_CODE.md` は、技術検証で実際に動かして合格したソースの全文**（約 2,500 行）。PDFium 層・座標変換・焼込・確定出力・検証・確認用PDF・印刷・テスト用PDF生成が入っている。該当モジュールはゼロから書かず、ここからコピーして始める。全文を一度に読まず、`## ファイル名` の見出しで必要な節だけ読むこと。

## 絶対に破ってはいけない制約

1. **原本PDFを絶対に書き換えない。** 原本は読取専用で開く。保存先が原本と同じパスなら拒否する。
2. **確定出力は「黒い図形を重ねる」実装禁止。** 全ページを画像化してピクセルに焼き込み、自前ライターで新規PDFを書く（ARCHITECTURE §7）。
3. **一時ファイルを作らない。** `Path.GetTempPath` / `Path.GetTempFileName` / AppData / レジストリの使用禁止。処理はオンメモリ。
4. **ログ・例外メッセージ・設定に、文書内容・ファイル名・パスを書かない。**
5. **外部通信をしない。** 更新確認・テレメトリも禁止。
6. **第三者依存は PDFium（`bblanchon.PDFium.Win32`）のみ。** NuGet パッケージを追加しない（テストプロジェクトの xUnit 系は例外）。特に iText / MuPDF / Ghostscript / Poppler / QuestPDF / Qt 系 / `Pdfium.Net.SDK` は混入禁止。PDFsharp・SkiaSharp・System.Text.Json も使わない。
7. **座標は「表示ページ座標(pt)」だけで持つ。** `/Rotate` を自前計算してよいのは `CoordMap` の1箇所だけ（ARCHITECTURE §2）。
8. **PDFium の呼び出しは専用スレッド1本から。** UI スレッドや Task から直接 P/Invoke しない（ARCHITECTURE §3）。

## 技術スタック

- C# / WPF / **.NET Framework 4.8**（`net48`, x64 のみ）。Windows 10/11 に同梱のランタイムを使う。
- PDF の読込・描画・ページ取込・添付: PDFium を自前 P/Invoke で呼ぶ。パッケージは `bblanchon.PDFium.Win32` **159.0.8152** に固定（上げる時は全テストをやり直す）。
- 確定出力: 自前の最小 PDF ライター。JPEG 化は WPF 内蔵の `JpegBitmapEncoder`（WIC）、可逆圧縮は `DeflateStream` + zlib ヘッダ。
- JSON: 標準の `DataContractJsonSerializer`。
- リボン: 標準の `System.Windows.Controls.Ribbon`。
- 配布: EXE + `pdfium.dll` + ライセンス文書の ZIP。単一EXE化（自己展開）はしない。

## ディレクトリ

```
src/Kuronuri.Core/        PDFium層・モデル・Undo・確定出力・検証・ハイブリッド保存（UI非依存）
src/Kuronuri.App/         WPF アプリ本体
tests/Kuronuri.Core.Tests/  xUnit テスト
docs/                     設計書
```

## ビルドとテスト

```
dotnet build Kuronuri.sln -c Release
dotnet test Kuronuri.sln -c Release
```

## net48 でのコーディング注意

- `LangVersion` は 12.0 固定。`ImplicitUsings` は無効。`Nullable` は有効。
- 使ってよい: record / init（`IsExternalInit` のポリフィルを1ファイル置く）、パターンマッチ、file-scoped namespace、global using。
- 使えない: `required` メンバー、インターフェイスの既定実装、`Index`/`Range`（`^1`, `..`）、`Span<T>` 前提の API、`LibraryImport`、`await using`、`Math.Clamp`、`System.Text.Json`、`ZLibStream`。
- P/Invoke は `DllImport`（x64 のみなので関数側に呼び出し規約の指定は不要）。コールバックのデリゲートには `[UnmanagedFunctionPointer(CallingConvention.Cdecl)]` を付け、必ずフィールドで保持する（GC で回収されるとクラッシュする）。
- 大きなピクセルバッファは `Marshal.AllocHGlobal` で確保して再利用し、解放前にゼロクリアする（`NativeBuffer`）。
- `BitmapSource.Create` を繰り返す処理（確定出力・印刷）では 8 ページごとに `GC.Collect(); GC.WaitForPendingFinalizers();` を呼ぶ。呼ばないとメモリが数百 MB 膨らむ。
- 名前空間 `Kuronuri.Core.Geometry` と WPF の `System.Windows.Media.Geometry` 型は名前が衝突する。後者は完全修飾名で書く。
- record の等値比較はリストを参照で比べる。箇所の同一性は `Id` で判定する。

## 作業の進め方

- `docs/TASKS.md` を上から順に進め、完了したタスクにチェックを入れる。
- Core の機能には必ずテストを付ける。テスト用PDFは `TestPdfFactory` で生成し、実在の文書をリポジトリに入れない。
- 「未検証」の事項は ARCHITECTURE §17 にある。該当箇所を実装したら結果を §17 に書き込む。
- UI 文言は日本語。コード中の識別子・コメントは英語か日本語どちらでもよいが、ファイル内で統一する。
