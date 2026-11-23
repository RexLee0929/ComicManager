## **{NAME}** **{VERSION}**

{TAGLINE}

把漫画站点下载的 EPUB 批量整理成 Kavita / Komga 能正确识别的 CBZ 库，并为已有 CBZ 补齐元数据。全部在本地完成；刮削与阅读方向判断按需联网，不发送文件内容。

**主要功能**

- EPUB → CBZ：按 spine 取真实页序，图片字节原样拷贝，不重编码、不掉画质
- 双服务器格式：Kavita（Volume 排序）/ Komga（Number 排序）/ 双兼容，可互转
- 元数据刮削：Bangumi / MangaUpdates / MangaDex 三个来源任选，按系列匹配，查不到就手填
- 写回方式：替换包内 ComicInfo.xml（图片不动，原文件可备份为 .bak）；Komga 另写 series.json 与系列封面
- 阅读方向自动判断：结合始发国线索和页面比例判断，必要时联网查证；结果写入 ComicInfo
- 繁转简：内置 OpenCC 词表，含假名的日文标题整条保留
- 预演：只读元数据不写文件，先看命名与 XML 结果再动手

**快捷键**

Ctrl+O 添加源目录　Ctrl+Shift+O 更换输出目录　Ctrl+E 打开输出目录
Ctrl+L 清空日志　Ctrl+Shift+L 清空预演结果　F5 刷新界面　Alt+F4 退出

{NAME} 是免费软件，依据 MIT License 分发。
© 2026 {AUTHOR}　保留所有权利。
