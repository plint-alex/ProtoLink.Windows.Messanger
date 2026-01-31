using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using ProtoLink.Windows.Messanger.Services;
using ProtoLink.Windows.Messanger.Models;
using Microsoft.Extensions.Logging;

namespace ProtoLink.Windows.Messanger.ViewModels
{
    public class AddContactViewModel : ViewModelBase
    {
        private readonly IAuthService _authService;
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private string _userId = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isLoading = false;
        private bool _isEmbedded = false;

        public AddContactViewModel(IAuthService authService, HttpClient httpClient, ILogger logger, bool isEmbedded = false)
        {
            _authService = authService;
            _httpClient = httpClient;
            _logger = logger;
            _isEmbedded = isEmbedded;

            AddContactCommand = new RelayCommand(async _ => await AddContactAsync(), _ => !string.IsNullOrWhiteSpace(UserId) && !_isLoading);
            CloseCommand = new RelayCommand(_ => OnCloseRequested?.Invoke());
        }

        public string UserId
        {
            get => _userId;
            set
            {
                _userId = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public bool IsEmbedded => _isEmbedded;

        public ICommand AddContactCommand { get; }
        public ICommand CloseCommand { get; }

        public event Action? OnContactAdded;
        public event Action? OnCloseRequested;

        private async Task AddContactAsync()
        {
            if (_authService.CurrentToken == null) return;

            IsLoading = true;
            StatusMessage = "Adding contact...";

            try
            {
                // Validate user ID format
                if (!Guid.TryParse(UserId.Trim(), out Guid contactUserId))
                {
                    StatusMessage = "Invalid user ID format. Please enter a valid GUID.";
                    return;
                }

                // Check if user is trying to add themselves
                if (contactUserId == _authService.CurrentToken.UserId)
                {
                    StatusMessage = "You cannot add yourself as a contact.";
                    return;
                }

                // First, get or create the user's contacts container
                var userId = _authService.CurrentToken.UserId;
                var response = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
                {
                    ParentIds = new[] { SystemEntities.Contacts, userId }
                });

                var results = await response.Content.ReadFromJsonAsync<System.Collections.Generic.List<GetEntitiesResult>>();
                var userContacts = results?.FirstOrDefault();

                Guid? userContactsId = null;
                if (userContacts == null)
                {
                    // Create user contacts container
                    var addResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
                    {
                        Code = "UserContacts",
                        ParentIds = new[] { SystemEntities.Contacts, userId }
                    });
                    var addResult = await addResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    userContactsId = addResult.GetProperty("id").GetGuid();
                }
                else
                {
                    userContactsId = userContacts.Id;
                }

                if (!userContactsId.HasValue)
                {
                    StatusMessage = "Failed to create user contacts container.";
                    return;
                }

                // Check if contact already exists
                var existingContactResponse = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
                {
                    ParentIds = new[] { userContactsId.Value, contactUserId },
                    IncludeValues = true
                });
                var existingContacts = await existingContactResponse.Content.ReadFromJsonAsync<System.Collections.Generic.List<GetEntitiesResult>>();
                if (existingContacts?.Any() == true)
                {
                    var existingContact = existingContacts.First();
                    // Check if contact has userid value
                    bool hasUserIdValue = false;
                    if (existingContact.Values != null)
                    {
                        foreach (var val in existingContact.Values)
                        {
                            if (val is Models.EntityValue entityValue && entityValue.Type == "StringValue")
                            {
                                string stringValue = entityValue.Value?.ToString() ?? "";
                                if (stringValue.StartsWith("userid:"))
                                {
                                    hasUserIdValue = true;
                                    break;
                                }
                            }
                        }
                    }
                    
                    // If contact exists but doesn't have userid value, add it
                    if (!hasUserIdValue)
                    {
                        var addValueResponse = await _httpClient.PostAsJsonAsync($"api/Entities/AddValue/{existingContact.Id}", new AddValueContract
                        {
                            Type = TypeOfValue.String,
                            Value = $"userid:{contactUserId}",
                            ParentIds = Array.Empty<Guid>()
                        });
                        
                        if (addValueResponse.IsSuccessStatusCode)
                        {
                            StatusMessage = "Contact updated with user ID.";
                        }
                    }
                    
                    StatusMessage = "Contact already exists.";
                    OnContactAdded?.Invoke();
                    return;
                }

                // Try to get the contact user's name (this is optional, we can use the user ID as fallback)
                string contactName = contactUserId.ToString();

                // Add the contact
                var addContactResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
                {
                    Code = contactName,
                    ParentIds = new[] { userContactsId.Value, contactUserId },
                    Values = new List<AddValueContract>
                    {
                        new AddValueContract { Type = TypeOfValue.String, Value = $"userid:{contactUserId}", ParentIds = Array.Empty<Guid>() }
                    }
                });

                if (addContactResponse.IsSuccessStatusCode)
                {
                    StatusMessage = "Contact added successfully!";
                    UserId = string.Empty; // Clear the input
                    OnContactAdded?.Invoke();
                }
                else
                {
                    StatusMessage = "Failed to add contact. Please check the user ID and try again.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding contact");
                StatusMessage = "An error occurred while adding the contact.";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}