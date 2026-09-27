# GitHub 与官网发布

## 仓库内容

将本源码目录的内容上传到一个新的 GitHub 仓库。保留 README、.gitignore、源码、测试、音频和第三方许可；不要上传开发工作目录的完整副本。新增代码已按维护者决定采用 MIT 许可，保留根目录 LICENSE；第三方声音的署名与许可独立保留。

## v1.0.0 安装发布

`v1.0.0` Release 已发布，说明中写明 Windows 11 x64、Work / Codex 范围及两个已核验版本。发布附件包含安装分发 ZIP、DSH-Desktop-Sounds-Setup.exe、INSTALL.txt、LICENSE.txt、GITHUB-SHA256.txt 和 SHA256-inside-ZIP.txt。前一校验文件对应 GitHub 附件名，后一校验文件对应 ZIP 内中文文件名。

普通用户应下载 EXE 或安装分发 ZIP；自动生成的 Source code ZIP 是开发源码。GitHub Releases 支持软件说明和二进制附件，见 [GitHub 官方说明](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases) 与 [发布步骤](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。

## 学社官网

使用 WEBSITE_COPY.md 的介绍，把下载按钮指向公开仓库的 Releases 页，把源码按钮指向仓库首页。可先使用 Releases 页链接，避免版本更新后网站仍指向过期客户端适配包。

发布仓库：[https://github.com/Redhood-Heqing/dsh-desktop-sounds](https://github.com/Redhood-Heqing/dsh-desktop-sounds)。安装包入口：[Releases](https://github.com/Redhood-Heqing/dsh-desktop-sounds/releases)。安装包尚未商业代码签名。学社官网接入时应保留许可文件与音频来源标注，并按实际展示内容填写学社署名。
