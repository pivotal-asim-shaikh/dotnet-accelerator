﻿namespace DotnetAccelerator.Persistence
{
    public enum DbType
    {
#if postgresql
        PostgreSQL,
#endif
        
#if mysql
        MySQL,
#endif
        
        SQLite
    }
}