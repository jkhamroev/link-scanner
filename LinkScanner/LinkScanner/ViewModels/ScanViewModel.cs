﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Vision.V1;
using LinkScanner.Models;
using LinkScanner.Views;
using Plugin.Connectivity;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace LinkScanner.ViewModels
{
    /// <summary>
    /// ViewModel of ScanPage
    /// </summary>
    public class ScanViewModel : BaseViewModel
    {
        // Embedded "secret" file name
        private const string FileName = "LinkScanner.google-credentials.json";
        private static string _apiKey;

        // URL pattern. Detects even there is no "www."
        private const string Pattern =
            @"(http:\/\/www\.|https:\/\/www\.|http:\/\/|https:\/\/)?[a-z0-9]+([\-\.]{1}[a-z0-9]+)*\.[a-z]{2,5}(:[0-9]{1,5})?(\/.*)?";

        #region Alternative Solution

        /* 
         * Detect even there is no "www" and "http(s)":
         * (http:\/\/www\.|https:\/\/www\.|http:\/\/|https:\/\/)?[a-z0-9]+([\-\.]{1}[a-z0-9]+)*\.[a-z]{2,5}(:[0-9]{1,5})?(\/.*)?
         * 
         * "htttp(s)" is required:
         * (http|https)://([\w+?\.\w+])+([a-zA-Z0-9\~\!\@\#\$\%\^\&\*\(\)_\-\=\+\\\/\?\.\:\;\'\,]*)?
         * 
         * "www" is required:
         * (?:(?:https?|ftp|file):\/\/|www\.|ftp\.)(?:\([-A-Z0-9+&@#\/%=~_|$?!:,.]*\)|[-A-Z0-9+&@#\/%=~_|$?!:,.])*(?:\([-A-Z0-9+&@#\/%=~_|$?!:,.]*\)|[A-Z0-9+&@#\/%=~_|$])
         */

        #endregion

        private bool _isVisible;

        /// <summary>
        /// Responsible for the visibility of "IndicatorImageSource" and "IndicatorMessage"
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        private string _indicatorImageSource;

        /// <summary>
        /// Path for the image which shows after scanning
        /// </summary>
        public string IndicatorImageSource
        {
            get => _indicatorImageSource;
            set => SetProperty(ref _indicatorImageSource, value);
        }

        private string _indicatorMessage;

        /// <summary>
        /// Text which shows after scanning
        /// </summary>
        public string IndicatorMessage
        {
            get => _indicatorMessage;
            set => SetProperty(ref _indicatorMessage, value);
        }

        /// <summary>
        /// Command to capture image with camera
        /// </summary>
        public ICommand CaptureCommand => new Command(CaptureButton_OnClicked);

        /// <summary>
        /// Command to load image from storage
        /// </summary>
        public ICommand LoadCommand => new Command(LoadButton_OnClicked);

        /// <summary>
        /// The command for "About app" button
        /// </summary>
        public ICommand AboutAppCommand => new Command(AboutAppButton_OnClicked);

        /// <summary>
        /// Initializes a property 'apiKey'
        /// </summary>
        public ScanViewModel()
        {
 
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();

                using (Stream stream = assembly.GetManifestResourceStream(FileName))
                using (StreamReader sr = new StreamReader(stream!))
                    _apiKey = sr.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                Task.Run(() => DisplayAlert("Internal error", "Text cannot be detected", "OK"));
            }
            catch (UnauthorizedAccessException)
            {
                Task.Run(() => DisplayAlert("Internal error", "Text cannot be detected", "OK"));
            }
            catch (IOException)
            {
                Task.Run(() => DisplayAlert("Internal error", "Text cannot be detected", "OK"));
            }
            catch (Exception)
            {
                Task.Run(() => DisplayAlert("Internal error", "Text cannot be detected", "OK"));
            }
        }

        /// <summary>
        /// Opens the camera to capture an image
        /// </summary>
        private async void CaptureButton_OnClicked()
        {
            IsVisible = false;

            if (!MediaPicker.IsCaptureSupported)
            {
                await DisplayAlert("No Camera", ":( No camera available.", "OK");
                return;
            }

            FileResult result;
            try
            {
                IsBusy = true;
                result = await MediaPicker.CapturePhotoAsync();
            }
            catch (NotImplementedException ex)
            {
                await DisplayAlert("Permission Denied", ex.Message, "OK");
                IsBusy = false;
                return;
            }

            if (result == null)
            {
                IsBusy = false;
                return;
            }

            AddToHistory(result.FullPath);
            IsBusy = false;
        }

        /// <summary>
        /// Opens the gallery to load the image
        /// </summary>
        private async void LoadButton_OnClicked()
        {
            IsVisible = false;

            FileResult result;
            try
            {
                IsBusy = true;
                result = await MediaPicker.PickPhotoAsync();
            }
            catch (NotImplementedException ex)
            {
                await DisplayAlert("Permission Denied", ex.Message, "OK");
                IsBusy = false;
                return;
            } // Occurs when button is clicked twice
            catch (InvalidOperationException)
            {
                return;
            }

            if (result == null)
            {
                IsBusy = false;
                return;
            }

            AddToHistory(result.FullPath);

            IsBusy = false;
        }

        /// <summary>
        /// Opens the page with the information about this app
        /// </summary>
        private async void AboutAppButton_OnClicked()
        {
            await PushAsync(new AboutPage());
        }

        /// <summary>
        /// Adds items to history page
        /// </summary>
        private async void AddToHistory(string imagePath)
        {
            var urlCollection = await RecognizeUrlAsync(imagePath);

            if (urlCollection != null)
            {
                IndicatorImageSource = "success.png";
                IndicatorMessage = "Result has been saved in history";

                foreach (var url in urlCollection)
                {
                    var item = new Item
                    {
                        ImagePath = imagePath,
                        Url = url,
                        CreationTime = DateTime.Now,
                        Id = Guid.NewGuid().ToString()
                    };

                    // Sending message to HistoryViewModel
                    MessagingCenter.Send(this, "AddToHistory", item);
                }
            }
            else
            {
                IndicatorImageSource = "error.png";
                IndicatorMessage = "URL hasn't been detected, but image has been saved in history";

                var item = new Item
                {
                    ImagePath = imagePath,
                    Url = "",
                    CreationTime = DateTime.Now,
                    Id = new Guid().ToString()
                };
                // Sending message to HistoryViewModel
                MessagingCenter.Send(this, "AddToHistory", item);
            }

            IsVisible = true;
        }

        /// <summary>
        /// Extracts the text from the specified image file by using
        /// the Computer Vision REST API.
        /// </summary>
        /// <param name="imagePath">Path of the image in the storage</param>
        async Task<IEnumerable<string>> RecognizeUrlAsync(string imagePath)
        {
            if (!CrossConnectivity.Current.IsConnected)
            {
                await DisplayAlert("No internet", "Internet connection failed", "OK");
                return null;
            }

            // For timeout of 10 seconds
            var callSettings = CallSettings.FromExpiration(Expiration.FromTimeout(TimeSpan.FromSeconds(10)));
            try
            {
                // Defining a ImageAnnotatorClient object
                var client = await new ImageAnnotatorClientBuilder {JsonCredentials = _apiKey}.BuildAsync();

                // Getting image from path
                var image = await Google.Cloud.Vision.V1.Image.FromFileAsync(imagePath);

                // Specifying the language
                var imageContext = new ImageContext {LanguageHints = {"en"}};

                // Detecting text on the image
                var textAnnotations = await client.DetectTextAsync(image, imageContext, 0, callSettings);

                // Extracting the text from response. Can throw ArgumentOutOfRangeException
                string result = textAnnotations[0].Description;

                var regex = new Regex(Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

                // Extracting texts that fits the pattern
                var matches = regex.Matches(result);

                var myCollection = (from Match match in matches
                    select match.Value).ToList();

                if (myCollection.Any())
                    return myCollection;
            }
            catch (InvalidOperationException)
            {
                await DisplayAlert("No internet", "Internet connection failed", "OK");
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
            catch (Grpc.Core.RpcException)
            {
                await DisplayAlert("Error", "Timeout of detecting the text", "OK");
            }
            catch (AnnotateImageException)
            {
                await DisplayAlert("Error", "Service currently unavailable. Try again later", "OK");
            }
            catch (FileNotFoundException)
            {
                await DisplayAlert("Error", "Image not found", "OK");
            }
            catch (UnauthorizedAccessException)
            {
                await DisplayAlert("Error", "Access to the storage has been denied", "OK");
            }
            catch (IOException)
            {
                await DisplayAlert("Error", "Something went wrong with file", "OK");
            }
            catch (Exception)
            {
                await DisplayAlert("Error", "Something went wrong. Try again later", "OK");
            }

            return null;
        }
    }
}