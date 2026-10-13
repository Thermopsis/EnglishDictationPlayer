using System.Collections.Generic;

namespace PhrasePlayer {
    public static class Localization {
        public static readonly Dictionary<string, string> English = new Dictionary<string, string> {
            { "切换下一段时自动播放（含右方向键）", "Play on Next (including Right arrow)" },
            { "教师模式", "Teacher mode" }, { "保存课件", "Save lesson" },
            { "打开音频或课件", "Open audio or lesson" },
            { "音频或听写课件|*.edlesson;*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.mp4|听写课件|*.edlesson|所有文件|*.*", "Audio or lesson|*.edlesson;*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.mp4|Dictation lesson|*.edlesson|All files|*.*" },
            { "听写课件|*.edlesson", "Dictation lesson|*.edlesson" },
            { "请使用 .edlesson 扩展名保存课件。", "Please save with the .edlesson extension." },
            { "正在保存课件…", "Saving lesson..." }, { "课件已保存：", "Lesson saved: " }, { "课件保存失败：", "Could not save lesson: " },
            { "已恢复课件：{0} 段，未重新自动分段。", "Restored {0} lesson phrases without resegmenting." },
            { "未保存的课件", "Unsaved lesson" },
            { "分段修改尚未保存为课件。继续会丢失这些修改。\n请取消后点击“保存课件”，或确定放弃修改。", "Phrase edits have not been saved as a lesson. Continuing discards them.\nCancel and click Save lesson, or OK to discard edits." },
            { "合并下一段", "Merge next" }, { "在此拆分", "Split here" }, { "撤销", "Undo" },
            { "放大 +", "Zoom +" }, { "缩小 −", "Zoom -" }, { "显示全部", "Fit all" },
            { "起点 −0.1s", "Start -0.1s" }, { "起点 +0.1s", "Start +0.1s" }, { "终点 −0.1s", "End -0.1s" }, { "终点 +0.1s", "End +0.1s" },
            { "开头预留（秒）", "Leading padding (s)" }, { "显示边界微调（±0.1 秒）", "Show boundary controls (+/-0.1 s)" },
            { "按片段长度自动留白", "Adaptive gap" }, { "留白倍数", "Gap multiplier" }, { "留白 = 片段时长 × 倍数，最多 120 秒", "Gap = duration x multiplier, up to 120 s" },
            { "英语听写播放器", "English Dictation Player" },
            { " [静音测试]", " [Silent test]" },
            { "打开音频", "Open audio" }, { "设置", "Settings" },
            { "上一段", "Previous" }, { "播放", "Play" }, { "下一段", "Next" },
            { "暂停", "Pause" }, { "继续", "Resume" }, { "停止", "Stop" },
            { "分段设置", "Segmentation" }, { "自动分段设置", "Segmentation settings" },
            { "播放次数", "Repeats" }, { "间隔", "Gap" }, { "秒", "s" },
            { "播完进入下一段", "Auto next" }, { "从头播放到尾", "Play entire audio" },
            { "倍速", "Speed" }, { "播放倍速", "Playback speed" },
            { "当前短语", "Current phrase" }, { "重复间隔秒数", "Repeat gap in seconds" },
            { "1 次", "1 time" }, { "2 次", "2 times" }, { "3 次", "3 times" },
            { "5 次", "5 times" }, { "10 次", "10 times" }, { "无限", "Infinite" },
            { "0.5 倍", "0.5x" }, { "0.8 倍", "0.8x" }, { "1.0 倍", "1.0x" },
            { "1.2 倍", "1.2x" }, { "1.5 倍", "1.5x" }, { "2.0 倍", "2.0x" },
            { "MP3 / WAV / M4A / AAC / WMA · 也可以把音频拖进窗口", "MP3 / WAV / M4A / AAC / WMA · Drop an audio file here" },
            { "单击选短语 · 拖动选片段 · 滚轮缩放", "Click to select · Drag a range · Scroll to zoom" },
            { "空格：播放 / 暂停    ← →：切换片段    Enter：从头重播", "Space: Play / Pause    Left / Right: Previous / Next    Enter: Replay phrase" },
            { "音频文件|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.mp4|所有文件|*.*", "Audio files|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.mp4|All files|*.*" },
            { "正在分析音频…", "Analyzing audio…" }, { "正在分析音频… ", "Analyzing audio… " },
            { "正在关闭…", "Closing…" },
            { "打开失败，原有音频仍可使用。", "Could not open the file. The previous audio is still available." },
            { "无法打开这段音频。请确认文件有效且系统支持其格式。\n\n", "Cannot open this audio. Check that the file is valid and its format is supported.\n\n" },
            { "第 {0} 段   {1}", "Phrase {0}   {1}" }, { "已自动分为 ", "Detected " },
            { " 段。单击选段，拖动可自由选取片段。", " phrases. Click a phrase or drag to select a range." },
            { "没有检测到语音，可以拖动手动选段。", "No speech detected. Drag to select a range." },
            { "未找到明显声音。可调整分段设置或手动选段。", "No clear speech detected. Adjust segmentation or select a range manually." },
            { "手动片段", "Manual range" }, { "{0} / {1} 段", "Phrase {0} / {1}" },
            { "   {0:0.00} — {1:0.00} 秒   ·   {2:0.0} 秒", "   {0:0.00} — {1:0.00} s   ·   {2:0.0} s" },
            { "已暂停 · Enter 从片段开头重播", "Paused · Enter replays the selected phrase" },
            { "已暂停", "Paused" }, { "已停止", "Stopped" },
            { "全文连续播放 · ", "Playing entire audio · " }, { " 倍速 · Esc 停止", "x · Esc to stop" },
            { "正在播放 · 第 {0} 遍{1} · {2} 倍速", "Playing · {0}{1} · {2}x" },
            { " / 无限循环", " / infinite" }, { "播放失败：", "Playback failed: " },
            { "全文播放完成", "Entire audio completed" },
            { "本段播放完成 · Enter 重播，方向键切换片段", "Phrase completed · Enter to replay, Left / Right to change phrase" },
            { "间隔 {0:0.0} 秒 · 点击播放按钮可停止", "Gap: {0:0.0} s · Click Stop to end repeats" },
            { "常用分段推荐", "Segmentation preset" }, { "自定义", "Custom" },
            { "短语练习：1–5 秒", "Short phrases: 1–5 s" },
            { "常规听写：2–8 秒（推荐）", "Dictation: 2–8 s (recommended)" },
            { "长句练习：4–15 秒", "Long sentences: 4–15 s" },
            { "片段最短秒数", "Minimum phrase seconds" }, { "片段最长秒数", "Maximum phrase seconds" },
            { "合并过短片段（优先连接下一段）", "Merge short phrases (prefer next)" },
            { "合并过短片段", "Merge short phrases" },
            { "分段停顿毫秒", "Pause length in milliseconds" }, { "静音阈值", "Silence threshold" },
            { "最短片段（秒）", "Minimum length (s)" }, { "最长片段（秒）", "Maximum length (s)" },
            { "分段停顿（毫秒）", "Pause length (ms)" }, { "静音阈值（相对 dB）", "Silence threshold (dB)" },
            { "最短值用于合并短段；关闭合并会保留短词。\n长停顿不强行连接；尾段或孤立短词可能不足最短值。\n按停顿分段，不识别文字。分得太碎可增大停顿。", "Merge short phrases below minimum; turn off to keep short words.\nLong pauses stay apart; isolated / final phrases may be shorter.\nSplits on pauses. Increase pause length if needed. No speech recognition." },
            { "重新分段", "Resegment" }, { "取消", "Cancel" },
            { "最短片段不能大于最长片段。", "Minimum length cannot exceed maximum length." },
            { "分段区间", "Duration range" },
            { "皮肤", "Theme" }, { "默认模式", "Default mode" }, { "黑色模式", "Dark mode" },
            { "语言", "Language" }, { "保存设置", "Save settings" },
            { "设置已保存", "Settings saved" },
            { "皮肤与语言将在保存后立即生效，下次打开时保留。", "Save to apply. Theme and language are remembered." },
            { "关于", "About" }, { "帮助", "Help" }, { "关闭", "Close" },
            { "打开一段音频，开始逐段听写", "Open audio to start phrase-by-phrase dictation" },
            { "音频波形：单击选择短语，拖动选择片段，滚轮缩放", "Audio waveform: click a phrase, drag a range, scroll to zoom" }
        };
        public static string Get(string text, string language) {
            string result;
            return language == "en" && English.TryGetValue(text, out result) ? result : text;
        }
        public static string ConvertExisting(string text, string language) {
            if (English.ContainsKey(text)) return Get(text, language);
            foreach (var entry in English) if (entry.Value == text) return Get(entry.Key, language);
            return text;
        }
    }
}
