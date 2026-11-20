using System.Security.Cryptography;

namespace DustBox;

internal sealed class MainForm : Form
{
    private readonly Panel dropPanel = new();
    private readonly Label dropLabel = new();
    private readonly Label statusLabel = new();
    private readonly ListBox logList = new();
    private readonly string[] startupPaths;
    private bool isProcessing;

    public MainForm(string[] startupPaths)
    {
        this.startupPaths = startupPaths;
        Text = "DustBox - 乱数上書き削除";
        ClientSize = new Size(800, 560);
        MinimumSize = new Size(640, 440);
        StartPosition = FormStartPosition.CenterScreen;
        Shown += async (_, _) =>
        {
            if (this.startupPaths.Length > 0)
            {
                await ProcessPathsAsync(this.startupPaths);
            }
        };

        dropPanel.SetBounds(20, 20, 744, 160);
        dropPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        dropPanel.AllowDrop = true;
        dropPanel.BackColor = SystemColors.ControlLight;
        dropPanel.BorderStyle = BorderStyle.FixedSingle;
        dropPanel.DragEnter += OnDragEnter;
        dropPanel.DragDrop += OnDragDrop;

        dropLabel.Dock = DockStyle.Fill;
        dropLabel.TextAlign = ContentAlignment.MiddleCenter;
        dropLabel.Font = new Font(Font.FontFamily, 15);
        dropLabel.Text = "ファイルやフォルダーをここにドロップ";
        dropLabel.AllowDrop = true;
        dropLabel.DragEnter += OnDragEnter;
        dropLabel.DragDrop += OnDragDrop;
        dropPanel.Controls.Add(dropLabel);

        var warningLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, 195),
            Text = "注意: SSD、クラウド同期、スナップショット等では、上書きによる消去を保証できません。"
        };

        statusLabel.SetBounds(20, 235, 600, 24);
        statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        statusLabel.Text = "未処理";

        var clearButton = new Button
        {
            Text = "ログを消去",
            Bounds = new Rectangle(660, 230, 104, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        clearButton.Click += (_, _) => logList.Items.Clear();

        logList.SetBounds(20, 270, 744, 260);
        logList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logList.HorizontalScrollbar = true;
        logList.IntegralHeight = false;

        Controls.AddRange([dropPanel, warningLabel, statusLabel, clearButton, logList]);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = !isProcessing && e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        await ProcessPathsAsync(paths);
    }

    private async Task ProcessPathsAsync(string[] paths)
    {
        if (isProcessing || paths.Length == 0)
        {
            return;
        }

        var preview = string.Join(Environment.NewLine, paths.Take(8).Select(path => $"- {path}"));
        if (paths.Length > 8)
        {
            preview += $"{Environment.NewLine}...ほか {paths.Length - 8} 件";
        }

        var answer = MessageBox.Show(
            this,
            $"{paths.Length} 件を乱数で1回上書きし、削除します。この操作は取り消せません。{Environment.NewLine}{Environment.NewLine}{preview}{Environment.NewLine}{Environment.NewLine}続行しますか？",
            "削除の確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        isProcessing = true;
        statusLabel.Text = "処理中...";
        IProgress<EraseResult> progress = new Progress<EraseResult>(result =>
        {
            logList.Items.Add($"{(result.Success ? "完了" : "失敗")}: {result.Path} - {result.Message}");
            logList.TopIndex = logList.Items.Count - 1;
        });

        try
        {
            var totals = await Task.Run(() =>
            {
                var succeeded = 0;
                var failed = 0;

                foreach (var path in paths)
                {
                    EraseEntry(path, result =>
                    {
                        progress.Report(result);
                        if (result.Success)
                        {
                            succeeded++;
                        }
                        else
                        {
                            failed++;
                        }
                    });
                }

                return (Succeeded: succeeded, Failed: failed);
            });

            statusLabel.Text = $"完了: {totals.Succeeded} 件成功、{totals.Failed} 件失敗";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "予期しないエラーが発生しました。";
            logList.Items.Add($"エラー: {ex.Message}");
            MessageBox.Show(this, ex.Message, "処理エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            isProcessing = false;
        }
    }

    private static bool EraseEntry(string path, Action<EraseResult> report)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("再解析ポイントは安全のため処理しません。");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                var allChildrenErased = true;
                foreach (var child in Directory.EnumerateFileSystemEntries(path))
                {
                    if (!EraseEntry(child, report))
                    {
                        allChildrenErased = false;
                    }
                }

                if (!allChildrenErased)
                {
                    report(new EraseResult(path, false, "未処理の項目が残っているため、フォルダーは削除しませんでした。"));
                    return false;
                }

                Directory.Delete(path);
                report(new EraseResult(path, true, "フォルダーを削除しました。"));
                return true;
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                throw new UnauthorizedAccessException("読み取り専用ファイルです。属性を解除してから再試行してください。");
            }

            if ((attributes & FileAttributes.Device) != 0)
            {
                throw new IOException("デバイスは処理できません。");
            }

            OverwriteWithRandomData(path);
            File.Delete(path);
            report(new EraseResult(path, true, "上書きして削除しました。"));
            return true;
        }
        catch (Exception ex)
        {
            report(new EraseResult(path, false, ex.Message));
            return false;
        }
    }

    private static void OverwriteWithRandomData(string path)
    {
        const int bufferSize = 1024 * 1024;
        var buffer = new byte[bufferSize];

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.SequentialScan | FileOptions.WriteThrough);

        var remaining = stream.Length;
        while (remaining > 0)
        {
            var bytesToWrite = (int)Math.Min(buffer.Length, remaining);
            RandomNumberGenerator.Fill(buffer.AsSpan(0, bytesToWrite));
            stream.Write(buffer, 0, bytesToWrite);
            remaining -= bytesToWrite;
        }

        stream.Flush(flushToDisk: true);
    }

    private sealed record EraseResult(string Path, bool Success, string Message);
}
