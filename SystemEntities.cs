using System;

namespace ProtoLink.Windows.Messanger
{
    public static class SystemEntities
    {
        public static readonly Guid Public = new Guid("00000000-0000-0000-0000-000000000001");
        public static readonly Guid Contacts = new Guid("00020000-0000-0000-0000-000000000000");
        public static readonly Guid Message = new Guid("00020001-0000-0000-0000-000000000000");
        public static readonly Guid Sent = new Guid("00020002-0000-0000-0000-000000000000");
        public static readonly Guid Received = new Guid("00020003-0000-0000-0000-000000000000");
        public static readonly Guid Year = new Guid("00020004-0000-0000-0000-000000000000");
        public static readonly Guid Month = new Guid("00020005-0000-0000-0000-000000000000");
        public static readonly Guid Day = new Guid("00020006-0000-0000-0000-000000000000");
        public static readonly Guid Hour = new Guid("00020007-0000-0000-0000-000000000000");
        public static readonly Guid Minute = new Guid("00020008-0000-0000-0000-000000000000");
    }
}

