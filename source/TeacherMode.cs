using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PhrasePlayer {
    public sealed partial class PlayerForm {
        readonly CheckBox teacher = new CheckBox();
        readonly Button saveLesson = new Button();
        OpenLesson openedLesson;
        string sourceAudioPath, originalAudioName, lessonPath;
        bool lessonDirty;
        void BuildTeacherTools(FlowLayoutPanel row) {
            teacher.Text = L("教师模式"); teacher.AutoSize = true; teacher.Checked = prefs.TeacherMode; teacher.Margin = new Padding(16, 8, 8, 0);
            StyleButton(saveLesson, L("保存课件"), false); saveLesson.Size = new Size(136, 36); saveLesson.Visible = teacher.Checked;
            row.Controls.Add(teacher); row.Controls.Add(saveLesson);
            teacher.CheckedChanged += delegate { prefs.TeacherMode = teacher.Checked; saveLesson.Visible = teacher.Checked; SavePreferences(); };
            saveLesson.Click += async delegate { await SaveLesson(); };
        }
        bool ConfirmDiscard() {
            if (!lessonDirty || (!teacher.Checked && lessonPath == null)) return true;
            return MessageBox.Show(this, L("分段修改尚未保存为课件。继续会丢失这些修改。\n请取消后点击“保存课件”，或确定放弃修改。"), L("未保存的课件"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
        }
        async Task SaveLesson() {
            if (loading || audio == null || sourceAudioPath == null) return;
            using (var dialog = new SaveFileDialog { Title = L("保存课件"), Filter = L("听写课件|*.edlesson"), DefaultExt = "edlesson", AddExtension = true, FileName = lessonPath == null ? Path.GetFileNameWithoutExtension(originalAudioName) + ".edlesson" : Path.GetFileName(lessonPath), OverwritePrompt = true }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!LessonFile.IsLesson(dialog.FileName)) { MessageBox.Show(this, L("请使用 .edlesson 扩展名保存课件。")); return; }
                Stop(); loading = true; UpdateEnabled(); status.Text = L("正在保存课件…");
                loadCancellation = new CancellationTokenSource(); var token = loadCancellation.Token;
                var snapshot = PhraseEditor.Copy(segments); var chosen = selection == null ? null : new Phrase(selection.Start, selection.End);
                try {
                    await Task.Run(() => LessonFile.Save(dialog.FileName, sourceAudioPath, originalAudioName, audio.Duration, snapshot, index, chosen, token));
                    lessonPath = dialog.FileName; lessonDirty = false; status.Text = L("课件已保存：") + Path.GetFileName(lessonPath);
                } catch (OperationCanceledException) { } catch (Exception error) {
                    if (!closing) MessageBox.Show(this, L("课件保存失败：") + error.Message, L("保存课件"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                } finally {
                    loading = false; loadCancellation.Dispose(); loadCancellation = null;
                    if (closing && !IsDisposed) Close(); else if (!closing) UpdateEnabled();
                }
            }
        }
        void RestoreLesson(LessonManifest manifest) {
            editHistory.Clear(); segments = manifest.Segments.ConvertAll(p => p.ToPhrase()); wave.Phrases = segments;
            if (segments.Count > 0) RebuildEdited(Math.Max(0, manifest.SelectedIndex));
            else { index = -1; selection = null; updating = true; phrases.Items.Clear(); updating = false; wave.Selected = null; selected.Text = L("没有检测到语音，可以拖动手动选段。"); }
            if (manifest.SelectedIndex < 0 && manifest.ManualSelection != null) { index = -1; selection = manifest.ManualSelection.ToPhrase(); selection.Manual = true; UpdateSelection(); wave.ShowPhrase(selection); }
            wave.EditTime = -1; lessonDirty = false; UpdateScroll(); wave.Invalidate();
            status.Text = string.Format(L("已恢复课件：{0} 段，未重新自动分段。"), segments.Count);
        }
    }
}
