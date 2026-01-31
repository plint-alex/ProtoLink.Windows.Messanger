using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using ProtoLink.Windows.Messanger.Models;
using ProtoLink.Windows.Messanger.Services;

namespace ProtoLink.Windows.Messanger.ViewModels
{
    public class MessengerViewModel : ViewModelBase
    {
        private readonly IAuthService _authService;
        private readonly HttpClient _httpClient;
        private readonly SignalRService _signalRService;
        private Contact? _selectedContact;
        private string _messageText = string.Empty;
        private Guid? _userContactsId;

        // Cache for container IDs to avoid repeated lookups
        private Guid? _mySentContainerId;
        private Guid? _myReceivedContainerId;
        private readonly Dictionary<Guid, Guid?> _partnerSentContainerIds = new();
        private readonly Dictionary<Guid, Guid?> _partnerReceivedContainerIds = new();
        
        // Cache for messages per contact
        private readonly Dictionary<Guid, List<MessageViewModel>> _cachedMessages = new();
        private readonly HashSet<Guid> _loadedContacts = new();

        public MessengerViewModel(IAuthService authService, HttpClient httpClient)
        {
            _authService = authService;
            _httpClient = httpClient;
            _signalRService = new SignalRService(
                _httpClient.BaseAddress?.ToString()?.TrimEnd('/') ?? "https://localhost:5001",
                authService.CurrentToken?.AccessToken ?? "");

            Contacts = new ObservableCollection<Contact>();
            Messages = new ObservableCollection<MessageViewModel>();
            SendCommand = new RelayCommand(async _ => await SendMessageAsync(), _ => SelectedContact != null && !string.IsNullOrWhiteSpace(MessageText));
            ReceiveCommand = new RelayCommand(async _ => await LoadMessagesAsync(), _ => SelectedContact != null);

            // Handle real-time message notifications
            _signalRService.MessageReceived += async () =>
            {
                // When a message command is received, refresh messages for the current contact
                if (SelectedContact != null)
                {
                    await LoadMessagesAsync();
                }
            };

            _ = InitializeAsync();
        }

        public ObservableCollection<Contact> Contacts { get; }
        public ObservableCollection<MessageViewModel> Messages { get; }

        public Contact? SelectedContact
        {
            get => _selectedContact;
            set
            {
                _selectedContact = value;
                OnPropertyChanged();
                if (value != null)
                {
                    // Load from cache if available, otherwise fetch from API (only on first open)
                    if (_cachedMessages.TryGetValue(value.Id, out var cachedMsgs))
                    {
                        Messages.Clear();
                        foreach (var msg in cachedMsgs)
                        {
                            Messages.Add(msg);
                        }
                    }
                    else if (!_loadedContacts.Contains(value.Id))
                    {
                        // First time opening this chat - fetch messages
                        _ = LoadMessagesAsync();
                    }
                }
                else
                {
                    Messages.Clear();
                }
            }
        }

        public string MessageText
        {
            get => _messageText;
            set { _messageText = value; OnPropertyChanged(); }
        }

        public ICommand SendCommand { get; }
        public ICommand ReceiveCommand { get; }

        // Cache for user logins to avoid repeated API calls
        private readonly Dictionary<Guid, string> _userLoginCache = new();

        private async Task<string> GetUserLoginAsync(Guid userId)
        {
            // Check cache first
            if (_userLoginCache.TryGetValue(userId, out var cachedLogin))
            {
                return cachedLogin;
            }

            try
            {
                var response = await _httpClient.GetAsync($"api/Authentication/GetUserById?userId={userId}");
                if (response.IsSuccessStatusCode)
                {
                    var user = await response.Content.ReadFromJsonAsync<JsonElement>();
                    if (user.TryGetProperty("login", out var loginElement))
                    {
                        var login = loginElement.GetString() ?? userId.ToString();
                        _userLoginCache[userId] = login;
                        return login;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetUserLogin error: {ex.Message}");
            }

            // Fallback to user ID if we can't get the login
            return userId.ToString();
        }

        public async Task InitializeAsync()
        {
            if (_authService.CurrentToken == null) return;

            // Connect to SignalR for real-time messaging
            try
            {
                await _signalRService.ConnectAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalR connection failed: {ex.Message}");
            }

            try
            {
                var userId = _authService.CurrentToken.UserId;
                var response = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
                {
                    ParentIds = new[] { SystemEntities.Contacts, userId }
                });

                var results = await response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
                var userContacts = results?.FirstOrDefault();

                if (userContacts == null)
                {
                    var addResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
                    {
                        Code = "UserContacts",
                        ParentIds = new[] { SystemEntities.Contacts, userId }
                    });
                    var addResult = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
                    _userContactsId = addResult.GetProperty("id").GetGuid();
                }
                else
                {
                    _userContactsId = userContacts.Id;
                }

                if (_userContactsId.HasValue)
                {
                    var contactsResponse = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
                    {
                        ParentIds = new[] { _userContactsId.Value },
                        IncludeValues = true // We need values to get the user ID
                    });
                    var contactsResults = await contactsResponse.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
                    
                    Contacts.Clear();
                    if (contactsResults != null)
                    {
                        foreach (var c in contactsResults)
                        {
                            Guid contactUserId = Guid.Empty;

                            // Get the user ID from the contact entity's values (stored by AddContactViewModel)
                            if (c.Values != null)
                            {
                                foreach (var val in c.Values)
                                {
                                    if (val is EntityValue entityValue && entityValue.Type == "StringValue")
                                    {
                                        string stringValue = null;
                                        if (entityValue.Value is string str)
                                        {
                                            stringValue = str;
                                        }
                                        else if (entityValue.Value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                                        {
                                            stringValue = jsonElement.GetString();
                                        }

                                        if (stringValue != null && stringValue.StartsWith("userid:"))
                                        {
                                            var userIdStr = stringValue.Substring(7);
                                            if (Guid.TryParse(userIdStr, out var parsedUserId))
                                            {
                                                contactUserId = parsedUserId;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            // If no userid value found, check if contact code is a valid GUID (for old contacts)
                            if (contactUserId == Guid.Empty && !string.IsNullOrEmpty(c.Code))
                            {
                                if (Guid.TryParse(c.Code, out var codeAsGuid))
                                {
                                    contactUserId = codeAsGuid;
                                }
                            }

                            // Only add contacts with valid user ID
                            if (contactUserId != Guid.Empty)
                            {
                                var contactLogin = await GetUserLoginAsync(contactUserId);
                                Contacts.Add(new Contact { Id = contactUserId, Name = contactLogin });
                            }
                        }
                    }

                    // Check if current user already exists in contacts list as a self-contact
                    // Query for entities that have both _userContactsId and userId as parents
                    var selfContactCheckResponse = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
                    {
                        ParentIds = new[] { _userContactsId.Value, userId }
                    });
                    var selfContactCheckResults = await selfContactCheckResponse.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
                    var selfContactEntity = selfContactCheckResults?.FirstOrDefault();

                    // Check if self-contact already exists in the Contacts collection (by checking if any contact has userId as Id)
                    var currentUserExistsInContacts = Contacts.Any(c => c.Id == userId);
                    
                    // Check if self-contact entity exists in Contacts but with entity ID (not userId)
                    var selfContactWithEntityId = selfContactEntity != null ? Contacts.FirstOrDefault(c => c.Id == selfContactEntity.Id) : null;
                    
                    // If self-contact entity exists but is in Contacts with entity ID, replace it with userId
                    if (selfContactEntity != null && selfContactWithEntityId != null && !currentUserExistsInContacts)
                    {
                        Contacts.Remove(selfContactWithEntityId);
                        var displayName = selfContactEntity.Code ?? _authService.CurrentToken?.Login ?? "Me";
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            displayName = "Me";
                        }
                        // Add contact using userId as the Id for message functionality
                        Contacts.Add(new Contact { Id = userId, Name = displayName });
                    }
                    // If self-contact entity doesn't exist in the database, create it
                    else if (selfContactEntity == null && !currentUserExistsInContacts)
                    {
                        var userLogin = _authService.CurrentToken?.Login ?? "Me";
                        var displayName = string.IsNullOrWhiteSpace(userLogin) ? "Me" : userLogin;

                        // Create a contact entity for the current user
                        var addSelfContactResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
                        {
                            Code = displayName,
                            ParentIds = new[] { _userContactsId.Value, userId },
                            Values = new List<AddValueContract>
                            {
                                new AddValueContract { Type = TypeOfValue.String, Value = $"userid:{userId}", ParentIds = Array.Empty<Guid>() }
                            }
                        });
                        
                        if (addSelfContactResponse.IsSuccessStatusCode)
                        {
                            // Add contact using userId as the Id so messages work correctly
                            // This allows SendMessageAsync to use SelectedContact.Id as partnerUserId
                            Contacts.Add(new Contact { Id = userId, Name = displayName });
                        }
                    }
                    // If self-contact entity exists in database but not in Contacts collection, add it
                    else if (selfContactEntity != null && selfContactWithEntityId == null && !currentUserExistsInContacts)
                    {
                        var displayName = selfContactEntity.Code ?? _authService.CurrentToken?.Login ?? "Me";
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            displayName = "Me";
                        }
                        // Add contact using userId as the Id for message functionality
                        Contacts.Add(new Contact { Id = userId, Name = displayName });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Initialize error: {ex}");
            }
        }

        private async Task<Guid> GetOrCreateUserContainer(Guid systemParentId, Guid userId, string code, bool grantWriteToOwner = false)
        {
            // Check cache first
            Guid? cachedId = null;
            if (systemParentId == SystemEntities.Sent && userId == _authService.CurrentToken?.UserId)
            {
                cachedId = _mySentContainerId;
            }
            else if (systemParentId == SystemEntities.Received && userId == _authService.CurrentToken?.UserId)
            {
                cachedId = _myReceivedContainerId;
            }
            else if (systemParentId == SystemEntities.Sent && userId != _authService.CurrentToken?.UserId)
            {
                _partnerSentContainerIds.TryGetValue(userId, out cachedId);
            }
            else if (systemParentId == SystemEntities.Received && userId != _authService.CurrentToken?.UserId)
            {
                _partnerReceivedContainerIds.TryGetValue(userId, out cachedId);
            }

            if (cachedId.HasValue && cachedId.Value != Guid.Empty)
            {
                return cachedId.Value;
            }

            // Not cached, query from server
            var response = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
            {
                ParentIds = new[] { systemParentId, userId }
            });
            var results = await response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
            if (results?.Any() == true)
            {
                var foundContainerId = results.First().Id;

                // Cache the result
                CacheContainerId(systemParentId, userId, foundContainerId);

                return foundContainerId;
            }

            var addResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
            {
                Code = code,
                ParentIds = new[] { systemParentId, userId }
            });

            if (!addResponse.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"AddEntity failed: {addResponse.StatusCode}");
                return Guid.Empty;
            }

            var addResult = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
            var createdContainerId = addResult.GetProperty("id").GetGuid();

            // Cache the result
            CacheContainerId(systemParentId, userId, createdContainerId);

            // Grant write permission to the owner user (for containers created for other users)
            if (grantWriteToOwner && createdContainerId != Guid.Empty)
            {
                await GrantPermissionAsync(createdContainerId, userId, canWrite: true);
            }

            return createdContainerId;
        }

        private void CacheContainerId(Guid systemParentId, Guid userId, Guid id)
        {
            if (systemParentId == SystemEntities.Sent && userId == _authService.CurrentToken?.UserId)
            {
                _mySentContainerId = id;
            }
            else if (systemParentId == SystemEntities.Received && userId == _authService.CurrentToken?.UserId)
            {
                _myReceivedContainerId = id;
            }
            else if (systemParentId == SystemEntities.Sent && userId != _authService.CurrentToken?.UserId)
            {
                _partnerSentContainerIds[userId] = id;
            }
            else if (systemParentId == SystemEntities.Received && userId != _authService.CurrentToken?.UserId)
            {
                _partnerReceivedContainerIds[userId] = id;
            }
        }
        
        private async Task GrantPermissionAsync(Guid entityId, Guid permissionForId, bool canWrite)
        {
            try
            {
                var permissionResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddPermission", new AddPermissionContract
                {
                    Id = entityId,
                    PermissionForId = permissionForId,
                    CanWrite = canWrite
                });
                
                if (!permissionResponse.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"AddPermission failed: {permissionResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GrantPermission error: {ex.Message}");
            }
        }

        private async Task SendMessageAsync()
        {
            if (SelectedContact == null || _authService.CurrentToken == null || string.IsNullOrWhiteSpace(MessageText)) return;

            try
            {
                var myUserId = _authService.CurrentToken.UserId;
                var partnerUserId = SelectedContact.Id;
                var now = DateTime.UtcNow;
                var messageText = MessageText;
                var myLogin = await GetUserLoginAsync(myUserId);

                // Add message optimistically to cache and UI immediately
                var optimisticMessage = new MessageViewModel
                {
                    Text = messageText,
                    Timestamp = now,
                    IsFromMe = true,
                    SenderName = myLogin
                };

                // Add to cache
                if (!_cachedMessages.ContainsKey(partnerUserId))
                {
                    _cachedMessages[partnerUserId] = new List<MessageViewModel>();
                }
                _cachedMessages[partnerUserId].Add(optimisticMessage);

                // Update UI immediately
                Messages.Add(optimisticMessage);

                // Clear input
                MessageText = string.Empty;

                // Send to API
                var addMsgResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
                {
                    Code = "Message",
                    ParentIds = new[] { SystemEntities.Message },
                    Values = new List<AddValueContract>
                    {
                        new AddValueContract { Type = TypeOfValue.String, Value = messageText, ParentIds = Array.Empty<Guid>() },
                        new AddValueContract { Type = TypeOfValue.DateTime, Value = now, ParentIds = Array.Empty<Guid>() },
                        new AddValueContract { Type = TypeOfValue.String, Value = $"sender:{myUserId}", ParentIds = Array.Empty<Guid>() },
                        new AddValueContract { Type = TypeOfValue.String, Value = $"receiver:{partnerUserId}", ParentIds = Array.Empty<Guid>() }
                    },
                    Permissions = new List<AddPermissionContract>
                    {
                        partnerUserId != myUserId ? new AddPermissionContract { PermissionForId = partnerUserId, CanWrite = false } : null
                    }.Where(p => p != null).ToList()
                });
                
                if (!addMsgResponse.IsSuccessStatusCode)
                {
                    // Remove optimistic message on failure
                    _cachedMessages[partnerUserId].Remove(optimisticMessage);
                    Messages.Remove(optimisticMessage);
                    MessageText = messageText; // Restore text
                    return;
                }

                // Message sent successfully - send notification command to other users
                try
                {
                    var commandResponse = await _httpClient.PostAsJsonAsync("api/commands/send", new
                    {
                        commandType = "message_sent",
                        targetUserId = partnerUserId.ToString(),
                        parameters = new
                        {
                            senderId = myUserId.ToString(),
                            messageText = messageText,
                            timestamp = now
                        }
                    });

                    if (commandResponse.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine("Notification command sent successfully");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to send notification command: {commandResponse.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error sending notification command: {ex.Message}");
                }

                // Message sent successfully - cache will be updated on next LoadMessagesAsync call
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send error: {ex}");
            }
        }

        private async Task LoadMessagesAsync()
        {
            if (SelectedContact == null || _authService.CurrentToken == null) return;

            var myUserId = _authService.CurrentToken.UserId;
            var partnerUserId = SelectedContact.Id;
            var myUserIdStr = myUserId.ToString();
            var partnerUserIdStr = partnerUserId.ToString();

            // Get all messages the user has access to (API filters by permissions)
            var allMessagesResponse = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
            {
                ParentIds = new[] { SystemEntities.Message },
                IncludeValues = true
            });

            if (!allMessagesResponse.IsSuccessStatusCode)
            {
                return;
            }

            var allMessageEntities = await allMessagesResponse.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
            if (allMessageEntities == null) return;

            // Filter messages: sent (I sent to partner) and received (partner sent to me)
            var sentMessages = new List<GetEntitiesResult>();
            var receivedMessages = new List<GetEntitiesResult>();

            foreach (var message in allMessageEntities)
            {
                if (message.Values == null) continue;

                string? sender = null;
                string? receiver = null;

                foreach (var val in message.Values)
                {
                    if (val is EntityValue entityValue && entityValue.Type == "StringValue")
                    {
                        string stringValue = null;
                        if (entityValue.Value is string str)
                        {
                            stringValue = str;
                        }
                        else if (entityValue.Value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            stringValue = jsonElement.GetString();
                        }

                        if (stringValue != null)
                        {
                            if (stringValue.StartsWith("sender:"))
                            {
                                sender = stringValue.Substring(7);
                            }
                            else if (stringValue.StartsWith("receiver:"))
                            {
                                receiver = stringValue.Substring(9);
                            }
                        }
                    }
                }

                // Sent: I am sender, partner is receiver
                if (sender == myUserIdStr && receiver == partnerUserIdStr)
                {
                    sentMessages.Add(message);
                }
                // Received: Partner is sender, I am receiver
                else if (sender == partnerUserIdStr && receiver == myUserIdStr)
                {
                    receivedMessages.Add(message);
                }
            }

            // Get user logins for display
            var myLogin = await GetUserLoginAsync(myUserId);
            var partnerLogin = await GetUserLoginAsync(partnerUserId);

            // Convert to MessageViewModel and cache
            var messageList = new List<MessageViewModel>();

            // Process sent messages
            foreach (var msg in sentMessages)
            {
                var messageViewModel = ParseMessageToViewModel(msg, isFromMe: true, senderName: myLogin);
                if (messageViewModel != null)
                {
                    messageList.Add(messageViewModel);
                }
            }

            // Process received messages
            foreach (var msg in receivedMessages)
            {
                var messageViewModel = ParseMessageToViewModel(msg, isFromMe: false, senderName: partnerLogin);
                if (messageViewModel != null)
                {
                    messageList.Add(messageViewModel);
                }
            }

            // Cache messages for this contact
            _cachedMessages[partnerUserId] = messageList.OrderBy(x => x.Timestamp).ToList();
            _loadedContacts.Add(partnerUserId);

            // Update UI
            Messages.Clear();
            foreach (var m in _cachedMessages[partnerUserId])
            {
                Messages.Add(m);
            }
        }

        private MessageViewModel? ParseMessageToViewModel(GetEntitiesResult message, bool isFromMe, string senderName)
        {
            if (message.Values == null) return null;

            var textValue = "";
            var dateValue = DateTime.MinValue;

            foreach (var val in message.Values)
            {
                if (val is EntityValue entityValue)
                {
                    if (entityValue.Type == "StringValue")
                    {
                        string stringValue = entityValue.Value?.ToString() ?? "";
                        if (!stringValue.StartsWith("sender:") && !stringValue.StartsWith("receiver:"))
                        {
                            textValue = stringValue;
                        }
                    }
                    else if (entityValue.Type == "DateTimeValue")
                    {
                        if (entityValue.Value != null)
                        {
                            string dateString = null;
                            if (entityValue.Value is string str)
                            {
                                dateString = str;
                            }
                            else if (entityValue.Value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                dateString = jsonElement.GetString();
                            }

                            if (dateString != null)
                            {
                                DateTime.TryParse(dateString, out dateValue);
                            }
                        }
                    }
                }
            }

            return new MessageViewModel
            {
                Text = textValue,
                Timestamp = dateValue,
                IsFromMe = isFromMe,
                SenderName = senderName
            };
        }

        public async Task DisposeAsync()
        {
            if (_signalRService != null)
            {
                await _signalRService.DisconnectAsync();
            }
        }
    }

    public class Contact
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class MessageViewModel
    {
        public string Text { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsFromMe { get; set; }
    }
}
