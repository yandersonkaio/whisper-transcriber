using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Whisper.net;
using Whisper.net.Ggml;
using System.Threading.Tasks;
using System;

namespace WhisperTranscriber
{
    public partial class MainWindow : Window
    {
        private Stopwatch transcriptionStopwatch;

        public MainWindow()
        {
            InitializeComponent();
            InitializeLanguageComboBox();
            transcriptionStopwatch = new Stopwatch();
        }

        private void InitializeLanguageComboBox()
        {
            var languages = new string[]
            {
                "auto", "pt", "en", "es", "fr", "de", "it", "ja", "zh", "ru"
            };

            LanguageComboBox.ItemsSource = languages;
            LanguageComboBox.SelectedItem = "auto";
        }

        private void BrowseFfmpegButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Selecione a pasta do FFmpeg",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Selecione a pasta"
            };

            if (dialog.ShowDialog() == true)
            {
                FfmpegPathTextBox.Text = Path.GetDirectoryName(dialog.FileName);
            }
        }

        private void BrowseAudioButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Arquivos de áudio (*.mp3;*.wav;*.ogg;*.m4a;*.flac)|*.mp3;*.wav;*.ogg;*.m4a;*.flac|Todos os arquivos (*.*)|*.*",
                Title = "Selecione o arquivo de áudio"
            };

            if (dialog.ShowDialog() == true)
            {
                AudioPathTextBox.Text = dialog.FileName;
            }
        }

        private async Task DownloadModelAsync(string modelType)
        {
            try
            {
                var modelFile = $"ggml-{modelType}.bin";
                var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelFile);

                var nativeLibPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper.dll");
                if (!File.Exists(nativeLibPath))
                {
                    MessageBox.Show($"Biblioteca nativa não encontrada: {nativeLibPath}. Instale o pacote Whisper.net.Runtime ou coloque a biblioteca manualmente.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!File.Exists(modelPath))
                {
                    StatusTextBlock.Text = $"Baixando modelo {modelFile}...";
                    ProgressBar.Visibility = Visibility.Visible;
                    ProgressBar.IsIndeterminate = true;

                    using var httpClient = new HttpClient();
                    var downloader = new WhisperGgmlDownloader(httpClient);

                    var ggmlType = modelType switch
                    {
                        "tiny" => GgmlType.Tiny,
                        "base" => GgmlType.Base,
                        "small" => GgmlType.Small,
                        "medium" => GgmlType.Medium,
                        "large" => GgmlType.LargeV1,
                        _ => GgmlType.Base
                    };

                    using var modelStream = await downloader.GetGgmlModelAsync(ggmlType);
                    using var fileWriter = File.OpenWrite(modelPath);
                    await modelStream.CopyToAsync(fileWriter);

                    ProgressBar.IsIndeterminate = false;
                    ProgressBar.Visibility = Visibility.Collapsed;
                    StatusTextBlock.Text = $"Modelo {modelFile} baixado com sucesso.";
                }
            }
            catch (Exception ex)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Erro ao baixar o modelo: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<string> TranscribeAudioAsync(string audioPath, string modelType, string language)
        {
            try
            {
                if (!File.Exists(audioPath))
                {
                    MessageBox.Show($"Arquivo de áudio não encontrado: {audioPath}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }

                await DownloadModelAsync(modelType);

                string modelFile = $"ggml-{modelType}.bin";
                string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelFile);

                if (!File.Exists(modelPath))
                {
                    MessageBox.Show($"Modelo não encontrado: {modelPath}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }

                StatusTextBlock.Text = $"Carregando modelo {modelFile}...";
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = true;

                using var whisperFactory = WhisperFactory.FromPath(modelPath);
                using var processor = whisperFactory.CreateBuilder()
                    .WithLanguage(language)
                    .Build();

                using var audioStream = File.OpenRead(audioPath);

                StatusTextBlock.Text = "Iniciando transcrição...";
                ProgressBar.IsIndeterminate = false;

                var segments = new List<string>();
                transcriptionStopwatch.Restart();

                await foreach (var segment in processor.ProcessAsync(audioStream))
                {
                    segments.Add(segment.Text);
                    TimeElapsedTextBlock.Text = $"Tempo decorrido: {transcriptionStopwatch.Elapsed:mm\\:ss}";
                }

                transcriptionStopwatch.Stop();
                ProgressBar.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = "Transcrição concluída.";
                TimeElapsedTextBlock.Text = $"Concluído em: {transcriptionStopwatch.Elapsed:mm\\:ss}";

                return string.Join("\n", segments);
            }
            catch (Exception ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Erro durante a transcrição: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void SaveTranscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Arquivos de texto (*.txt)|*.txt|Documentos Word (*.docx)|*.docx|Todos os arquivos (*.*)|*.*",
                DefaultExt = ".txt",
                Title = "Salvar transcrição como"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, TranscriptionTextBox.Text);
                    StatusTextBlock.Text = "Transcrição salva com sucesso!";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao salvar o arquivo: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopyTranscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TranscriptionTextBox.Text))
            {
                Clipboard.SetText(TranscriptionTextBox.Text);
                StatusTextBlock.Text = "Transcrição copiada para área de transferência!";
            }
        }

        private bool SetupFfmpeg(string ffmpegPath)
        {
            try
            {
                if (!Directory.Exists(ffmpegPath))
                {
                    MessageBox.Show($"Diretório FFmpeg não encontrado: {ffmpegPath}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                var ffmpegExePath = Path.Combine(ffmpegPath, "ffmpeg.exe");
                if (!File.Exists(ffmpegExePath))
                {
                    MessageBox.Show($"Arquivo ffmpeg.exe não encontrado em: {ffmpegPath}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                var path = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
                if (!path.Contains(ffmpegPath))
                {
                    path += ";" + ffmpegPath;
                    Environment.SetEnvironmentVariable("PATH", path, EnvironmentVariableTarget.Process);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao configurar FFmpeg: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async void TranscribeButton_Click(object sender, RoutedEventArgs e)
        {
            TranscriptionTextBox.Clear();
            SaveTranscriptionButton.Visibility = Visibility.Collapsed;
            TranscribeButton.IsEnabled = false;
            CopyTranscriptionButton.IsEnabled = false;

            try
            {
                var audioPath = AudioPathTextBox.Text;
                var ffmpegPath = FfmpegPathTextBox.Text;
                var modelType = ModelTypeComboBox.SelectedItem?.ToString() ?? "base";
                var language = LanguageComboBox.SelectedItem?.ToString() ?? "auto";

                if (string.IsNullOrWhiteSpace(audioPath))
                {
                    MessageBox.Show("Selecione um arquivo de áudio primeiro", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!SetupFfmpeg(ffmpegPath)) return;

                string tempWavPath = Path.Combine(Path.GetTempPath(), $"temp_audio_{Guid.NewGuid()}.wav");
                if (!audioPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || !IsValidWavFile(audioPath))
                {
                    try
                    {
                        StatusTextBlock.Text = "Convertendo áudio para formato WAV...";
                        ProgressBar.Visibility = Visibility.Visible;
                        ProgressBar.IsIndeterminate = true;

                        Process process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = Path.Combine(ffmpegPath, "ffmpeg.exe"),
                                Arguments = $"-i \"{audioPath}\" -acodec pcm_s16le -ar 16000 -ac 1 \"{tempWavPath}\" -y -hide_banner -loglevel error",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };

                        process.Start();
                        await process.WaitForExitAsync();

                        if (process.ExitCode != 0)
                        {
                            var error = await process.StandardError.ReadToEndAsync();
                            throw new Exception($"FFmpeg error: {error}");
                        }

                        audioPath = tempWavPath;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Erro ao converter o áudio: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    finally
                    {
                        ProgressBar.Visibility = Visibility.Collapsed;
                    }
                }

                var transcription = await TranscribeAudioAsync(audioPath, modelType, language);
                if (!string.IsNullOrWhiteSpace(transcription))
                {
                    TranscriptionTextBox.Text = transcription;
                    SaveTranscriptionButton.Visibility = Visibility.Visible;
                    CopyTranscriptionButton.Visibility = Visibility.Visible;
                    CopyTranscriptionButton.IsEnabled = true;
                }
            }
            finally
            {
                TranscribeButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;

                try
                {
                    var tempFiles = Directory.GetFiles(Path.GetTempPath(), "temp_audio_*.wav");
                    foreach (var file in tempFiles)
                    {
                        File.Delete(file);
                    }
                }
                catch { }
            }
        }

        private bool IsValidWavFile(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                byte[] header = new byte[12];
                stream.Read(header, 0, 12);

                return header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
                       header[8] == 'W' && header[9] == 'A' && header[10] == 'V' && header[11] == 'E';
            }
            catch
            {
                return false;
            }
        }
    }
}