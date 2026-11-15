# セキュリティについて / Security Policy

## 報告していただきたい問題

公文書くろぬりは、黒塗りした情報が外部に漏れないことが最も大切なソフトウェアです。次のような問題を見つけたら、**公開の Issue ではなく**、下の方法で非公開で報告してください。

- 開示用PDFから、黒塗りした部分の文字・画像・その他の情報が読み取れる
- 開示用PDFに、原本の文字情報・メタデータ・注釈・添付ファイル・しおりなどが残る
- 出力の検証が、問題のある開示用PDFを合格にしてしまう
- 黒塗りの位置が画面の表示とずれて出力される
- 一時ファイル・ログ・レジストリ・通信など、端末や外部に文書の内容が残る・送られる
- 細工した PDF を開くと、任意のコードが実行される・アプリが異常な動作をする

## 報告の方法

GitHub の非公開の脆弱性報告を使ってください: <https://github.com/efukan/kuronuri/security/advisories/new>

- 再現の手順、使った版、Windows の版を書いてください。
- 再現用の PDF が必要な場合は、**実際の文書ではなく、架空の内容で作った PDF** を添付してください。
- 個人で開発しているため、返信や修正までにお時間をいただく場合があります。修正版を公開するまで、内容の公開はお控えください。

## 対象の版

最新のリリースだけを修正の対象とします。

---

## Reporting a vulnerability (English)

Kuronuri is a PDF redaction tool. If you find a way to recover redacted content from an exported PDF, a case where the output verification passes an unsafe file, or any leak of document content to disk or the network, please report it privately through GitHub's private vulnerability reporting: <https://github.com/efukan/kuronuri/security/advisories/new>. Please attach only synthetic test PDFs, never real documents. Only the latest release is supported.
