using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ProtoLink.Windows.Messanger.Models
{
    public class GetEntitiesContract
    {
        public Guid[]? Ids { get; set; }
        public Guid[]? ParentIds { get; set; }
        public Guid[]? IdsToFindParents { get; set; }
        public int Skip { get; set; }
        public int? Take { get; set; }
        public bool IncludeValues { get; set; }
    }

    public class GetEntitiesResult
    {
        public Guid Id { get; set; }
        public string? Code { get; set; }
        public List<EntityValue>? Values { get; set; }
    }

    public class AddEntityContract
    {
        public string? Code { get; set; }
        public bool CodeIsUnique { get; set; }
        public Guid[] ParentIds { get; set; } = Array.Empty<Guid>();
        public int Order { get; set; }
        public bool Hidden { get; set; }
        public List<AddValueContract>? Values { get; set; }
        public List<AddPermissionContract>? Permissions { get; set; }
    }

    public enum TypeOfValue
    {
        String,
        Int,
        Double,
        DateTime,
        File
    }

    public class AddValueContract
    {
        public TypeOfValue Type { get; set; }
        public object? Value { get; set; }
        public Guid[] ParentIds { get; set; } = Array.Empty<Guid>();
    }

    public class GetEntityResult
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }
        
        [JsonPropertyName("values")]
        public List<EntityValue> Values { get; set; } = new();
    }

    public class EntityValue
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }
        
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        
        [JsonPropertyName("value")]
        public object? Value { get; set; }
        
        [JsonPropertyName("parents")]
        public IEnumerable<Guid> Parents { get; set; } = new List<Guid>();
    }

    public class AddPermissionContract
    {
        public Guid Id { get; set; }
        public Guid PermissionForId { get; set; }
        public bool CanWrite { get; set; }
    }
}

