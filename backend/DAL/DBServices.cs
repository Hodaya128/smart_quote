using Microsoft.Data.SqlClient;
using System.Data;
using comviaServer.Model;

namespace comviaServer.DAL;

/// <summary>
/// שכבת DAL — גישה לבסיס נתונים בלבד, ללא לוגיקה עסקית.
/// </summary>
public class DBServices
{
    private readonly string _connectionString;

    public DBServices(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("myProjDB")
            ?? throw new Exception("Connection string 'myProjDB' not found");
    }

    private SqlConnection Connect() => new SqlConnection(_connectionString);

    // =============================================
    // ===== USERS =====
    // =============================================

    public List<User> GetAllUsers()
    {
        var list = new List<User>();

        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetAllUsers", con)
        {
            CommandType = CommandType.StoredProcedure
        };

        con.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(new User
            {
                UserID      = Convert.ToInt32(reader["User_ID"]),
                UserName    = reader["User_Name"]?.ToString() ?? "",
                Email       = reader["Email"]?.ToString() ?? "",
                Type        = reader["Type"]?.ToString() ?? "",
                CreatedDate = Convert.ToDateTime(reader["Created_Date"])
            });

        return list;
    }

    public User? GetUserById(int id)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetUserById", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@User_ID", id);

        con.Open();
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return MapUser(reader);
    }

    public User? GetUserByEmail(string email)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetUserByEmail", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@Email", email);

        con.Open();
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return MapUser(reader);
    }

    public User? InsertUser(User user)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_InsertUser", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@Email",       user.Email);
        cmd.Parameters.AddWithValue("@Password",    user.Password);
        cmd.Parameters.AddWithValue("@Type",        user.Type);
        cmd.Parameters.AddWithValue("@Created_Date", DateTime.UtcNow);

        var idParam = new SqlParameter("@NewID", SqlDbType.Int) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(idParam);

        con.Open();
        cmd.ExecuteNonQuery();

        int newId = (int)idParam.Value;
        return GetUserById(newId);
    }

    public void UpdateUserToken(int userId, string? token)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_UpdateUserToken", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@User_ID", userId);
        cmd.Parameters.AddWithValue("@Token",  (object?)token ?? DBNull.Value);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    // sp_UpdateUser / sp_UpdateUserPassword / sp_DeleteUser מוגדרות ב-SQL/create_procedures.sql
    // עם פרמטר @UserID (הגרסאות הישנות ב-DB הפנו לעמודה UserID שלא קיימת ונכשלו).
    private void ExecUserIdSproc(string sproc, int id, params (string Name, object Value)[] extra)
    {
        using var con = Connect();
        using var cmd = new SqlCommand(sproc, con) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserID", id);
        foreach (var p in extra) cmd.Parameters.AddWithValue(p.Name, p.Value);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void UpdateUser(int id, User u) =>
        ExecUserIdSproc("sp_UpdateUser", id, ("@UserName", u.UserName), ("@Email", u.Email), ("@Type", u.Type));

    public void UpdateUserPassword(int id, string password) =>
        ExecUserIdSproc("sp_UpdateUserPassword", id, ("@Password", password));

    public void DeleteUser(int id) =>
        ExecUserIdSproc("sp_DeleteUser", id);

    // שליפת משתמש לפי טוקן — משמש את RequireRoleAttribute לאימות בקשות.
    // לא מחזיר את עמודת הסיסמה.
    public User? GetUserByToken(string token)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetUserByToken", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@Token", token);

        con.Open();
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new User
        {
            UserID      = Convert.ToInt32(reader["User_ID"]),
            UserName    = reader["User_Name"]?.ToString() ?? "",
            Email       = reader["Email"]?.ToString() ?? "",
            Type        = reader["Type"]?.ToString() ?? "",
            CreatedDate = Convert.ToDateTime(reader["Created_Date"])
        };
    }

    private User MapUser(SqlDataReader r) => new User
    {
        UserID      = Convert.ToInt32(r["User_ID"]),
        UserName    = r["User_Name"]?.ToString() ?? "",
        Email       = r["Email"]?.ToString() ?? "",
        Password    = r["Password"]?.ToString() ?? "",
        Token       = r["Token"] == DBNull.Value ? null : r["Token"]?.ToString(),
        Type        = r["Type"]?.ToString() ?? "",
        CreatedDate = Convert.ToDateTime(r["Created_Date"])
    };

    // =============================================
    // ===== CUSTOMERS =====
    // =============================================

    public List<Customer> GetAllCustomers()
    {
        var list = new List<Customer>();

        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetAllCustomers", con)
        {
            CommandType = CommandType.StoredProcedure
        };

        con.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(MapCustomer(reader));

        return list;
    }

    public Customer? GetCustomerById(int id)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetCustomerById", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@CustomerID", id);

        con.Open();
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return MapCustomer(reader);
    }

    public int InsertCustomer(Customer c)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_InsertCustomer", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@CustomerName", c.CustomerName);
        cmd.Parameters.AddWithValue("@Email",        (object?)c.Email   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Phone",        (object?)c.Phone   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Address",      (object?)c.Address ?? DBNull.Value);

        var idParam = new SqlParameter("@NewID", SqlDbType.Int) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(idParam);

        con.Open();
        cmd.ExecuteNonQuery();
        return (int)idParam.Value;
    }

    public void UpdateCustomer(int id, Customer c)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_UpdateCustomer", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@CustomerID",   id);
        cmd.Parameters.AddWithValue("@CustomerName", c.CustomerName);
        cmd.Parameters.AddWithValue("@Email",        (object?)c.Email   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Phone",        (object?)c.Phone   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Address",      (object?)c.Address ?? DBNull.Value);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void DeleteCustomer(int id)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_DeleteCustomer", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@CustomerID", id);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    private Customer MapCustomer(SqlDataReader r) => new Customer
    {
        CustomerID   = Convert.ToInt32(r["CustomerID"]),
        CustomerName = r["CustomerName"]?.ToString() ?? "",
        Email        = r["Email"] == DBNull.Value ? "" : r["Email"]?.ToString() ?? "",
        Phone        = r["Phone"] == DBNull.Value ? "" : r["Phone"]?.ToString() ?? "",
        Address      = r["Address"] == DBNull.Value ? null : r["Address"]?.ToString()
    };

    // =============================================
    // ===== COMPONENTS =====
    // =============================================

    public List<Component> GetAllComponents()
    {
        var list = new List<Component>();

        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetAllComponents", con)
        {
            CommandType = CommandType.StoredProcedure
        };

        con.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(MapComponent(reader));

        return list;
    }

    public Component? GetComponentBySku(string sku)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetComponentBySku", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ComponentSKU", sku);

        con.Open();
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return MapComponent(reader);
    }

    public void InsertComponent(Component c)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_InsertComponent", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ComponentSKU",   c.ComponentSKU);
        cmd.Parameters.AddWithValue("@Description",    c.Description);
        cmd.Parameters.AddWithValue("@BaseUnit",       c.BaseUnit);
        cmd.Parameters.AddWithValue("@AlternativeSKU", (object?)c.AlternativeSKU ?? DBNull.Value);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void UpdateComponent(string sku, Component c)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_UpdateComponent", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ComponentSKU",   sku);
        cmd.Parameters.AddWithValue("@Description",    c.Description);
        cmd.Parameters.AddWithValue("@BaseUnit",       c.BaseUnit);
        cmd.Parameters.AddWithValue("@AlternativeSKU", (object?)c.AlternativeSKU ?? DBNull.Value);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void DeleteComponent(string sku)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_DeleteComponent", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ComponentSKU", sku);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    private Component MapComponent(SqlDataReader r) => new Component
    {
        ComponentSKU   = r["ComponentSKU"]?.ToString() ?? "",
        Description    = r["Description"]?.ToString() ?? "",
        BaseUnit       = r["BaseUnit"]?.ToString() ?? "",
        AlternativeSKU = r["AlternativeSKU"] == DBNull.Value ? null : r["AlternativeSKU"]?.ToString()
    };

    // =============================================
    // ===== SUPPLIERS =====
    // =============================================

    public List<Supplier> GetAllSuppliers()
    {
        var list = new List<Supplier>();

        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetAllSuppliers", con)
        {
            CommandType = CommandType.StoredProcedure
        };

        con.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(MapSupplier(reader));

        return list;
    }

    public Supplier? GetSupplierById(int id)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetSupplierById", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@SupplierID", id);

        con.Open();
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return MapSupplier(reader);
    }

    public int InsertSupplier(Supplier s)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_InsertSupplier", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@SupplierName",  s.SupplierName);
        cmd.Parameters.AddWithValue("@WebsiteURL",    (object?)s.WebsiteUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RequiresLogin", s.RequiresLogin);
        cmd.Parameters.AddWithValue("@LoginAccount",  (object?)s.LoginAccount  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LoginUsername", (object?)s.LoginUsername ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LoginPassword", (object?)s.LoginPassword ?? DBNull.Value);

        var idParam = new SqlParameter("@NewID", SqlDbType.Int) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(idParam);

        con.Open();
        cmd.ExecuteNonQuery();
        return (int)idParam.Value;
    }

    public void UpdateSupplier(int id, Supplier s)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_UpdateSupplier", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@SupplierID",    id);
        cmd.Parameters.AddWithValue("@SupplierName",  s.SupplierName);
        cmd.Parameters.AddWithValue("@WebsiteURL",    (object?)s.WebsiteUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RequiresLogin", s.RequiresLogin);
        cmd.Parameters.AddWithValue("@LoginAccount",  (object?)s.LoginAccount  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LoginUsername", (object?)s.LoginUsername ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LoginPassword", (object?)s.LoginPassword ?? DBNull.Value);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void DeleteSupplier(int id)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_DeleteSupplier", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@SupplierID", id);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    private Supplier MapSupplier(SqlDataReader r) => new Supplier
    {
        SupplierID    = Convert.ToInt32(r["SupplierID"]),
        SupplierName  = r["SupplierName"]?.ToString() ?? "",
        WebsiteUrl    = r["WebsiteURL"]    == DBNull.Value ? "" : r["WebsiteURL"]?.ToString() ?? "",
        RequiresLogin = HasColumn(r, "RequiresLogin") && r["RequiresLogin"] != DBNull.Value && Convert.ToBoolean(r["RequiresLogin"]),
        LoginAccount  = !HasColumn(r, "LoginAccount")  || r["LoginAccount"]  == DBNull.Value ? null : r["LoginAccount"]?.ToString(),
        LoginUsername = !HasColumn(r, "LoginUsername") || r["LoginUsername"] == DBNull.Value ? null : r["LoginUsername"]?.ToString(),
        // ערך מוצפן — הפענוח מתבצע במתודות של Supplier (Model)
        LoginPassword = !HasColumn(r, "LoginPassword") || r["LoginPassword"] == DBNull.Value ? null : r["LoginPassword"]?.ToString()
    };

    private static bool HasColumn(SqlDataReader r, string name)
    {
        for (int i = 0; i < r.FieldCount; i++)
            if (r.GetName(i).Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // =============================================
    // ===== QUOTES =====
    // =============================================

    public List<Quote> GetAllQuotes()
    {
        var quotes = new List<Quote>();

        using var con = Connect();
        con.Open();

        using (var cmd = new SqlCommand("sp_GetAllQuotes", con)
        {
            CommandType = CommandType.StoredProcedure
        })
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                quotes.Add(MapQuote(reader));
        }

        if (quotes.Count == 0) return quotes;

        var quoteMap = quotes.ToDictionary(q => q.QuoteID);

        using (var cmd2 = new SqlCommand("sp_GetAllQuoteItems", con)
        {
            CommandType = CommandType.StoredProcedure
        })
        {
            using var reader2 = cmd2.ExecuteReader();
            while (reader2.Read())
            {
                int quoteId = Convert.ToInt32(reader2["QuoteID"]);
                if (quoteMap.TryGetValue(quoteId, out var quote))
                    quote.Items.Add(MapQuoteItem(reader2));
            }
        }

        return quotes;
    }

    public Quote? GetQuoteById(int id)
    {
        using var con = Connect();
        con.Open();

        Quote? quote = null;

        using (var cmd = new SqlCommand("sp_GetQuoteById", con)
        {
            CommandType = CommandType.StoredProcedure
        })
        {
            cmd.Parameters.AddWithValue("@QuoteID", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            quote = MapQuote(reader);
        }

        using (var cmd2 = new SqlCommand("sp_GetQuoteItemsByQuoteId", con)
        {
            CommandType = CommandType.StoredProcedure
        })
        {
            cmd2.Parameters.AddWithValue("@QuoteID", id);
            using var reader2 = cmd2.ExecuteReader();
            while (reader2.Read())
                quote.Items.Add(MapQuoteItem(reader2));
        }

        return quote;
    }

    public int InsertQuote(Quote quote)
    {
        using var con = Connect();
        con.Open();

        int newQuoteId;
        using (var cmd = new SqlCommand("sp_InsertQuote", con)
        {
            CommandType = CommandType.StoredProcedure
        })
        {
            cmd.Parameters.AddWithValue("@CustomerID",       quote.CustomerID);
            cmd.Parameters.AddWithValue("@Created_By",       quote.CreatedBy);
            cmd.Parameters.AddWithValue("@ComponentSKU",     (object?)quote.ComponentSKU   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status",           quote.Status);
            cmd.Parameters.AddWithValue("@Created_Date",      DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@TotalProductCost", quote.TotalProductCost);
            cmd.Parameters.AddWithValue("@TotalProfit",      quote.TotalProfit);
            cmd.Parameters.AddWithValue("@FinalTotalPrice",  quote.FinalTotalPrice);
            cmd.Parameters.AddWithValue("@SearchResultsJson", (object?)quote.SearchResultsJson ?? DBNull.Value);

            var idParam = new SqlParameter("@NewID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(idParam);

            cmd.ExecuteNonQuery();
            newQuoteId = (int)idParam.Value;
        }

        foreach (var item in quote.Items)
            InsertQuoteItem(con, newQuoteId, item);

        return newQuoteId;
    }

    public void UpdateQuoteSearchResults(int id, string searchResultsJson)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_UpdateQuoteSearchResults", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@QuoteID", id);
        cmd.Parameters.AddWithValue("@SearchResultsJson", searchResultsJson);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void UpdateQuote(int id, Quote quote)
    {
        using var con = Connect();
        con.Open();

        using (var cmd = new SqlCommand("sp_UpdateQuote", con)
        {
            CommandType = CommandType.StoredProcedure
        })
        {
            cmd.Parameters.AddWithValue("@QuoteID",          id);
            cmd.Parameters.AddWithValue("@CustomerID",       quote.CustomerID);
            cmd.Parameters.AddWithValue("@ComponentSKU",     (object?)quote.ComponentSKU   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status",           quote.Status);
            cmd.Parameters.AddWithValue("@TotalProductCost", quote.TotalProductCost);
            cmd.Parameters.AddWithValue("@TotalProfit",      quote.TotalProfit);
            cmd.Parameters.AddWithValue("@FinalTotalPrice",  quote.FinalTotalPrice);
            cmd.Parameters.AddWithValue("@SearchResultsJson", (object?)quote.SearchResultsJson ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        using (var cmd2 = new SqlCommand("sp_DeleteQuoteItems", con)
        {
            CommandType = CommandType.StoredProcedure
        })
        {
            cmd2.Parameters.AddWithValue("@QuoteID", id);
            cmd2.ExecuteNonQuery();
        }

        foreach (var item in quote.Items)
            InsertQuoteItem(con, id, item);
    }

    public void UpdateQuoteStatus(int id, string status)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_UpdateQuoteStatus", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@QuoteID", id);
        cmd.Parameters.AddWithValue("@Status",  status);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void DeleteQuote(int id)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_DeleteQuote", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@QuoteID", id);

        con.Open();
        cmd.ExecuteNonQuery();
    }

    private void InsertQuoteItem(SqlConnection con, int quoteId, QuoteItem item)
    {
        using var cmd = new SqlCommand("sp_InsertQuoteItem", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@QuoteID",            quoteId);
        cmd.Parameters.AddWithValue("@ComponentSKU",       (object?)item.ComponentSKU    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SupplierID",         item.SupplierID);
        cmd.Parameters.AddWithValue("@Quantity",           item.Quantity);
        cmd.Parameters.AddWithValue("@SupplyConfig",       item.SupplyConfig);
        cmd.Parameters.AddWithValue("@CostPriceMoment",    item.CostPriceMoment);
        cmd.Parameters.AddWithValue("@ProfitMargin",       item.ProfitMargin);
        cmd.Parameters.AddWithValue("@FinalPriceToClient", item.FinalPriceToClient);

        var idParam = new SqlParameter("@NewID", SqlDbType.Int) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(idParam);

        cmd.ExecuteNonQuery();
        item.ItemID = (int)idParam.Value;
    }

    // =============================================
    // ===== SETTINGS =====
    // =============================================

    public Dictionary<string, string> GetSettings()
    {
        var result = new Dictionary<string, string>();
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetAllSettings", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        con.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader["SettingKey"].ToString()!] = reader["SettingValue"]?.ToString() ?? "";
        return result;
    }

    public void SaveSetting(string key, string value)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_SaveSetting", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@SettingKey", key);
        cmd.Parameters.AddWithValue("@SettingValue", value);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    private Quote MapQuote(SqlDataReader r) => new Quote
    {
        QuoteID          = Convert.ToInt32(r["QuoteID"]),
        CustomerID       = Convert.ToInt32(r["CustomerID"]),
        CreatedBy        = HasColumn(r, "Created_By") && r["Created_By"] != DBNull.Value
                               ? Convert.ToInt32(r["Created_By"]) : 0,
        ComponentSKU     = r["ComponentSKU"] == DBNull.Value ? null : r["ComponentSKU"]?.ToString(),
        Status           = r["Status"]?.ToString() ?? "Draft",
        CreatedDate      = Convert.ToDateTime(r["CreatedDate"]),
        TotalProductCost = r["TotalProductCost"] == DBNull.Value ? 0 : Convert.ToDouble(r["TotalProductCost"]),
        TotalProfit      = r["TotalProfit"]      == DBNull.Value ? 0 : Convert.ToDouble(r["TotalProfit"]),
        FinalTotalPrice  = r["FinalTotalPrice"]  == DBNull.Value ? 0 : Convert.ToDouble(r["FinalTotalPrice"]),
        SearchResultsJson = r["SearchResultsJson"] == DBNull.Value ? null : r["SearchResultsJson"]?.ToString(),
        CreatedByName = r["CreatedByName"] == DBNull.Value ? null : r["CreatedByName"]?.ToString(),
        Customer = new Customer
        {
            CustomerID   = Convert.ToInt32(r["CustomerID"]),
            CustomerName = r["CustomerName"]?.ToString() ?? "",
            Email        = r["CustomerEmail"] == DBNull.Value ? "" : r["CustomerEmail"]?.ToString() ?? "",
            Phone        = r["CustomerPhone"] == DBNull.Value ? "" : r["CustomerPhone"]?.ToString() ?? ""
        }
    };

    private QuoteItem MapQuoteItem(SqlDataReader r) => new QuoteItem
    {
        ItemID             = Convert.ToInt32(r["QuoteItemID"]),
        QuoteID            = Convert.ToInt32(r["QuoteID"]),
        ComponentSKU       = r["ComponentSKU"] == DBNull.Value ? null : r["ComponentSKU"]?.ToString(),
        SupplierID         = Convert.ToInt32(r["SupplierID"]),
        SupplyConfig       = r["SupplyConfig"]?.ToString() ?? "",
        Quantity           = r["Quantity"] == DBNull.Value ? 0 : Convert.ToInt32(r["Quantity"]),
        CostPriceMoment    = r["CostPriceMoment"]    == DBNull.Value ? 0 : Convert.ToDouble(r["CostPriceMoment"]),
        ProfitMargin       = r["ProfitMargin"]       == DBNull.Value ? 0 : Convert.ToDouble(r["ProfitMargin"]),
        FinalPriceToClient = r["FinalPriceToClient"] == DBNull.Value ? 0 : Convert.ToDouble(r["FinalPriceToClient"])
    };
    // =============================================
    // ===== SETTINGS =====
    // =============================================

    public List<Setting> GetAllSettings()
    {
        var settingsList = new List<Setting>();

        using var con = Connect();

        using var cmd = new SqlCommand("sp_GetAllSettings", con)
        {
            CommandType = CommandType.StoredProcedure
        };

        con.Open();

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            Setting setting = new Setting
            {
                SettingKey = reader["SettingKey"].ToString(),

                SettingValue = Convert.ToDecimal(
                    reader["SettingValue"])
            };

            settingsList.Add(setting);
        }

        return settingsList;
    }

    public void UpdateSetting(string settingKey, decimal settingValue)
    {
        // sp_SaveSetting מבצעת upsert — מעדכנת אם קיים ומוסיפה אם לא.
        SaveSetting(settingKey, settingValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // שליפת ערך הגדרה בודד.
    public decimal GetSettingValue(string settingKey, decimal defaultValue = 0)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetSettingValue", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@SettingKey", settingKey);
        con.Open();
        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? defaultValue : Convert.ToDecimal(result);
    }

    // =============================================
    // ===== REFERENCING QUOTES LOOKUPS =====
    // =============================================
    // מזהי ההצעות שמשתמשות בישות (ספק/רכיב/לקוח) — למחיקה מדורגת לאחר אישור המשתמש.

    private List<int> QueryQuoteIds(string sproc, string paramName, object value)
    {
        var ids = new List<int>();
        using var con = Connect();
        using var cmd = new SqlCommand(sproc, con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue(paramName, value);
        con.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) ids.Add(Convert.ToInt32(reader[0]));
        return ids;
    }

    public List<int> GetQuoteIdsBySupplier(int supplierId) =>
        QueryQuoteIds("sp_GetQuoteIdsBySupplier", "@SupplierID", supplierId);

    public List<int> GetQuoteIdsByComponent(string sku) =>
        QueryQuoteIds("sp_GetQuoteIdsByComponent", "@ComponentSKU", sku);

    public List<int> GetQuoteIdsByCustomer(int customerId) =>
        QueryQuoteIds("sp_GetQuoteIdsByCustomer", "@CustomerID", customerId);

    // =============================================
    // ===== ALTERNATIVE COMPONENT LOOKUPS =====
    // =============================================
    // משמש את AlternativeComponent.

    public string? GetAlternativeSku(string componentSKU)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetAlternativeSku", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@ComponentSKU", componentSKU);
        con.Open();
        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : result.ToString();
    }

    public string? GetSupplierNameById(int supplierId)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetSupplierNameById", con)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@SupplierID", supplierId);
        con.Open();
        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : result.ToString();
    }

    // =============================================
    // ===== DASHBOARD WIDGETS =====
    // =============================================

    public List<SavedDashboardWidget> GetWidgetsByUserId(int userId)
    {
        var list = new List<SavedDashboardWidget>();
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetWidgetsByUserId", con) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserID", userId);
        con.Open();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapWidget(r));
        return list;
    }

    public SavedDashboardWidget? GetWidgetById(int id)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_GetWidgetById", con) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@WidgetID", id);
        con.Open();
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return MapWidget(r);
    }

    public int InsertWidget(SavedDashboardWidget w)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_InsertWidget", con) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserID", w.UserID);
        cmd.Parameters.AddWithValue("@Title", w.Title ?? "");
        cmd.Parameters.AddWithValue("@Type", w.Type ?? "chart");
        cmd.Parameters.AddWithValue("@ConfigJson", w.ConfigJson ?? "");
        cmd.Parameters.AddWithValue("@SortOrder", w.SortOrder);

        var idParam = new SqlParameter("@NewID", SqlDbType.Int) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(idParam);

        con.Open();
        cmd.ExecuteNonQuery();
        return (int)idParam.Value;
    }

    public void UpdateWidget(int id, SavedDashboardWidget w)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_UpdateWidget", con) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@WidgetID", id);
        cmd.Parameters.AddWithValue("@Title", w.Title ?? "");
        cmd.Parameters.AddWithValue("@Type", w.Type ?? "chart");
        cmd.Parameters.AddWithValue("@ConfigJson", w.ConfigJson ?? "");
        cmd.Parameters.AddWithValue("@SortOrder", w.SortOrder);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void DeleteWidget(int id)
    {
        using var con = Connect();
        using var cmd = new SqlCommand("sp_DeleteWidget", con) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@WidgetID", id);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    private SavedDashboardWidget MapWidget(SqlDataReader r) => new SavedDashboardWidget
    {
        WidgetID    = Convert.ToInt32(r["WidgetID"]),
        UserID      = Convert.ToInt32(r["UserID"]),
        Title       = r["Title"]?.ToString() ?? "",
        Type        = r["Type"]?.ToString() ?? "",
        ConfigJson  = r["ConfigJson"]?.ToString() ?? "",
        SortOrder   = r["SortOrder"] == DBNull.Value ? 0 : Convert.ToInt32(r["SortOrder"]),
        CreatedDate = Convert.ToDateTime(r["CreatedDate"]),
        UpdatedDate = r["UpdatedDate"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["UpdatedDate"])
    };
}
