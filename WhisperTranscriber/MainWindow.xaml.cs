using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Whisper.net;
using Whisper.net.Ggml;

namespace WhisperTranscriber
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
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
                Filter = "Arquivos de áudio (*.mp3;*.wav;*.ogg;*.m4a)|*.mp3;*.wav;*.ogg;*.m4a|Todos os arquivos (*.*)|*.*",
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
                    using var httpClient = new HttpClient();
                    var downloader = new WhisperGgmlDownloader(httpClient);
                    using var modelStream = await downloader.GetGgmlModelAsync(GgmlType.Base);
                    using var fileWriter = File.OpenWrite(modelPath);
                    await modelStream.CopyToAsync(fileWriter);
                    StatusTextBlock.Text = $"Modelo {modelFile} baixado com sucesso.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao baixar o modelo: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<string> TranscribeAudioAsync(string audioPath, string modelType)
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

                using var whisperFactory = WhisperFactory.FromPath(modelPath);
                using var processor = whisperFactory.CreateBuilder()
                    .WithLanguage("auto") // "pt"
                    .Build();

                using var audioStream = File.OpenRead(audioPath);

                StatusTextBlock.Text = "Iniciando transcrição...";

                var segments = new List<string>();

                await foreach (var segment in processor.ProcessAsync(audioStream))
                {
                    segments.Add(segment.Text);
                }

                StatusTextBlock.Text = "Transcrição concluída.";
                return string.Join("\n", segments);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro durante a transcrição: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }


        private void SaveTranscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Arquivos de texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*",
                DefaultExt = ".txt",
                Title = "Salvar transcrição como"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, TranscriptionTextBox.Text);
                    MessageBox.Show("Transcrição salva com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao salvar o arquivo: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private bool SetupFffmpeg(string ffmpegPath)
        {
            try
            {
                if (!Directory.Exists(ffmpegPath))
                {
                    MessageBox.Show($"Diretório FFmpeg não encontrado: {ffmpegPath}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                var path = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
                path += ";" + ffmpegPath;
                Environment.SetEnvironmentVariable("PATH", path, EnvironmentVariableTarget.Process);

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

            try
            {
                var audioPath = AudioPathTextBox.Text;
                var ffmpegPath = FfmpegPathTextBox.Text;
                var modelType = ModelTypeComboBox.SelectedItem?.ToString() ?? "base";

                if (!SetupFffmpeg(ffmpegPath)) return;

                string tempWavPath = Path.Combine(Path.GetTempPath(), "temp_audio.wav");
                if (!audioPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || !IsValidWavFile(audioPath))
                {
                    try
                    {
                        Process process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = Path.Combine(ffmpegPath, "ffmpeg.exe"),
                                Arguments = $"-i \"{audioPath}\" -acodec pcm_s16le -ar 16000 -ac 1 \"{tempWavPath}\" -y",
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        await process.WaitForExitAsync();
                        audioPath = tempWavPath;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Erro ao converter o áudio: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                var transcription = await TranscribeAudioAsync(audioPath, modelType);
                if (!string.IsNullOrWhiteSpace(transcription))
                {
                    TranscriptionTextBox.Text = transcription;
                    SaveTranscriptionButton.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                TranscribeButton.IsEnabled = true;
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