using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileReport.Models;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace FileReport.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const int YIELD_AFTER_FILES = 100; // Yield après chaque 100 fichiers
        private readonly ConcurrentQueue<string> _matchedFiles = new();
        private Task? _fileWriterTask;

        [ObservableProperty]
        private SearchParameters searchParameters = new();

        [ObservableProperty]
        private bool isSearching;

        [ObservableProperty]
        private int processedFilesCount;

        [ObservableProperty]
        private int matchedFilesCount;

        [ObservableProperty]
        private string newFilter = string.Empty;

        private CancellationTokenSource? cancellationTokenSource;
        private readonly SynchronizationContext? synchronizationContext;

        public MainViewModel()
        {
            synchronizationContext = SynchronizationContext.Current;

            // Si en mode auto, charger les paramètres et lancer la recherche
            if (App.AutoMode && !string.IsNullOrEmpty(App.ParametersFile))
            {
                LoadParametersFromFile(App.ParametersFile);
                _ = AutoSearchAsync();
            }
        }

        private async Task AutoSearchAsync()
        {
            try
            {
                await SearchAsync();
            }
            finally
            {
                // Fermer l'application après la recherche en mode auto
                Application.Current.Shutdown();
            }
        }

        private void LoadParametersFromFile(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var parameters = JsonSerializer.Deserialize<SearchParameters>(json, options);
                if (parameters != null)
                {
                    SearchParameters = parameters;
                    OnPropertyChanged(nameof(SearchParameters));
                }
                else
                {
                    MessageBox.Show("Le fichier de paramètres est invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    if (App.AutoMode)
                        Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des paramètres : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                if (App.AutoMode)
                    Application.Current.Shutdown();
            }
        }

        private void UpdateUICounters(int processed, int matched)
        {
            if (synchronizationContext != null)
            {
                synchronizationContext.Post(_ =>
                {
                    ProcessedFilesCount = processed;
                    MatchedFilesCount = matched;
                }, null);
            }
        }

        private ICommand? _searchCommand;
        public ICommand SearchCommand => _searchCommand ??= new AsyncRelayCommand(SearchAsync);

        [RelayCommand]
        private void AddFilter()
        {
            if (!string.IsNullOrWhiteSpace(NewFilter))
            {
                SearchParameters.FileFilters.Add(NewFilter);
                NewFilter = string.Empty;
            }
        }

        [RelayCommand]
        private void RemoveFilter(string filter)
        {
            SearchParameters.FileFilters.Remove(filter);
        }

        [RelayCommand]
        private void BrowseSearchPath()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Sélectionnez le dossier de recherche",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                SearchParameters.SearchPath = dialog.SelectedPath;
                OnPropertyChanged(nameof(SearchParameters));
            }
        }

        [RelayCommand]
        private void BrowseOutputPath()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Fichiers CSV (*.csv)|*.csv",
                Title = "Sélectionnez le fichier de rapport",
                DefaultExt = "csv",
                AddExtension = true,
                FileName = "FileReport_{yyyy-MM-dd}.csv"  // Format par défaut
            };

            if (dialog.ShowDialog() == true)
            {
                SearchParameters.OutputPath = dialog.FileName;
                OnPropertyChanged(nameof(SearchParameters));
            }
        }

        [RelayCommand]
        private void SaveParameters()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Fichiers JSON (*.json)|*.json",
                Title = "Sauvegarder les paramètres",
                DefaultExt = "json",
                AddExtension = true,
                FileName = "FileReport_params.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };
                    var json = JsonSerializer.Serialize(SearchParameters, options);
                    File.WriteAllText(dialog.FileName, json);
                    MessageBox.Show("Paramètres sauvegardés avec succès.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de la sauvegarde des paramètres : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void LoadParameters()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Fichiers JSON (*.json)|*.json",
                Title = "Charger les paramètres"
            };

            if (dialog.ShowDialog() == true)
            {
                LoadParametersFromFile(dialog.FileName);
            }
        }

        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchParameters.SearchPath) || !Directory.Exists(SearchParameters.SearchPath))
            {
                MessageBox.Show("Veuillez sélectionner un dossier de recherche valide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(SearchParameters.OutputPath))
            {
                MessageBox.Show("Veuillez sélectionner un fichier de sortie.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Remplacer les motifs de date dans le chemin de sortie
            var outputPath = ReplaceDatePatterns(SearchParameters.OutputPath);

            try
            {
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                {
                    MessageBox.Show("Le dossier du fichier de sortie n'existe pas.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Le chemin du fichier de sortie est invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsSearching = true;
            ProcessedFilesCount = 0;
            MatchedFilesCount = 0;

            cancellationTokenSource?.Dispose();
            cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;

            try
            {
                // Lancer la recherche sur un thread séparé
                await Task.Run(() =>
                {
                    var processedCount = 0;
                    var matchedCount = 0;
                    var lastUIUpdate = DateTime.Now;

                    // Créer l'en-tête du fichier
                    using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
                    {
                        writer.WriteLine("Nom;Chemin complet;Taille (octets);Date de création;Date de modification");
                    }

                    void ProcessDirectory(string currentPath)
                    {
                        // Traiter les fichiers
                        try
                        {
                            foreach (var file in Directory.GetFiles(currentPath))
                            {
                                if (token.IsCancellationRequested) return;

                                try
                                {
                                    var fileInfo = new FileInfo(file);
                                    processedCount++;

                                    if (SearchParameters.FileFilters.Count == 0 ||
                                        SearchParameters.FileFilters.Any(filter =>
                                            fileInfo.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        matchedCount++;
                                        using var writer = new StreamWriter(outputPath, true, Encoding.UTF8);
                                        writer.WriteLine($"{fileInfo.Name};{fileInfo.FullName};{fileInfo.Length};{fileInfo.CreationTime};{fileInfo.LastWriteTime}");
                                    }

                                    // Mettre à jour l'UI toutes les 100ms
                                    if ((DateTime.Now - lastUIUpdate).TotalMilliseconds > 100)
                                    {
                                        UpdateUICounters(processedCount, matchedCount);
                                        lastUIUpdate = DateTime.Now;
                                    }
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    Debug.WriteLine($"Accès refusé au fichier : {file}");
                                    continue;
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    Debug.WriteLine($"Erreur lors du traitement du fichier {file}: {ex.Message}");
                                    continue;
                                }
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            Debug.WriteLine($"Accès refusé aux fichiers du répertoire : {currentPath}");
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Debug.WriteLine($"Erreur lors de l'accès aux fichiers du répertoire {currentPath}: {ex.Message}");
                        }

                        // Traiter les sous-répertoires
                        try
                        {
                            foreach (var dir in Directory.GetDirectories(currentPath))
                            {
                                if (token.IsCancellationRequested) return;

                                try
                                {
                                    ProcessDirectory(dir);
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    Debug.WriteLine($"Accès refusé au répertoire : {dir}");
                                    continue;
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    Debug.WriteLine($"Erreur lors du traitement du répertoire {dir}: {ex.Message}");
                                    continue;
                                }
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            Debug.WriteLine($"Accès refusé à la liste des sous-répertoires de : {currentPath}");
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Debug.WriteLine($"Erreur lors de l'accès aux sous-répertoires de {currentPath}: {ex.Message}");
                        }
                    }

                    // Démarrer la recherche récursive
                    ProcessDirectory(SearchParameters.SearchPath);

                    // Mise à jour finale des compteurs
                    UpdateUICounters(processedCount, matchedCount);
                }, token);

                if (!token.IsCancellationRequested && !App.AutoMode)
                {
                    MessageBox.Show($"Recherche terminée !\n\nFichiers traités : {ProcessedFilesCount}\nFichiers trouvés : {MatchedFilesCount}",
                        "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Recherche annulée.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Une erreur est survenue : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSearching = false;
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
            }
        }

        private string ReplaceDatePatterns(string path)
        {
            // Recherche des motifs entre accolades
            var regex = new Regex(@"\{([^}]+)\}");
            return regex.Replace(path, match =>
            {
                try
                {
                    // Essaie de formater la date avec le motif fourni
                    return DateTime.Now.ToString(match.Groups[1].Value);
                }
                catch
                {
                    // Si le format est invalide, retourne le motif tel quel
                    return match.Value;
                }
            });
        }

        [RelayCommand]
        private void CancelSearch()
        {
            cancellationTokenSource?.Cancel();
        }
    }
}
