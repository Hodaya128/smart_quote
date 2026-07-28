-- =====================================================================
-- פרוצדורות חדשות לפרויקט קומוויה — מחליפות את כל ה-SQL שהיה בקוד.
-- להריץ פעם אחת ב-SSMS על הדאטהבייס של הפרויקט (igroup35_prod).
-- CREATE OR ALTER — בטוח להריץ שוב אם צריך לעדכן.
-- =====================================================================

-- ===== Users =====

-- תיקון: הפרוצדורות הקיימות ב-DB הפנו לעמודה UserID שלא קיימת (שם העמודה: User_ID)
-- ולכן עדכון/מחיקת משתמש נכשלו עם "Invalid column name 'UserID'".

CREATE OR ALTER PROCEDURE sp_UpdateUser
    @UserID INT,
    @UserName NVARCHAR(100),
    @Email NVARCHAR(100),
    @Type NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Users
    SET User_Name = @UserName, Email = @Email, Type = @Type
    WHERE User_ID = @UserID;
END
GO

CREATE OR ALTER PROCEDURE sp_UpdateUserPassword
    @UserID INT,
    @Password NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Users
    SET Password = @Password
    WHERE User_ID = @UserID;
END
GO

CREATE OR ALTER PROCEDURE sp_DeleteUser
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM Users
    WHERE User_ID = @UserID;
END
GO

-- שליפת משתמש לפי טוקן (לאימות בקשות בשרת). לא מחזירה את עמודת הסיסמה.
CREATE OR ALTER PROCEDURE sp_GetUserByToken
    @Token NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT User_ID, User_Name, Email, Type, Created_Date
    FROM Users
    WHERE Token = @Token;
END
GO

-- ===== Quotes =====

-- תיקון: הגרסה הקודמת ב-DB החזירה את שם היוצר (CreatedByName) אבל לא את העמודה
-- Created_By עצמה — ולכן סינון ההצעות למתמחר בשרת קיבל תמיד CreatedBy=0
-- וכל המשתמשים ראו את כל ההצעות. כאן מחזירים גם את Created_By.

CREATE OR ALTER PROCEDURE sp_GetAllQuotes
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        q.QuoteID,
        q.CustomerID,
        q.Created_By,
        q.ComponentSKU,
        q.Status,
        q.CreatedDate,
        q.TotalProductCost,
        q.TotalProfit,
        q.FinalTotalPrice,
        q.SearchResultsJson,
        u.User_Name AS CreatedByName,
        c.CustomerName,
        c.Email AS CustomerEmail,
        c.Phone AS CustomerPhone
    FROM Quotes q
    LEFT JOIN Customers c ON c.CustomerID = q.CustomerID
    LEFT JOIN Users u ON u.User_ID = q.Created_By
    ORDER BY q.QuoteID DESC;
END
GO

CREATE OR ALTER PROCEDURE sp_GetQuoteById
    @QuoteID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        q.QuoteID,
        q.CustomerID,
        q.Created_By,
        q.ComponentSKU,
        q.Status,
        q.CreatedDate,
        q.TotalProductCost,
        q.TotalProfit,
        q.FinalTotalPrice,
        q.SearchResultsJson,
        u.User_Name AS CreatedByName,
        c.CustomerName,
        c.Email AS CustomerEmail,
        c.Phone AS CustomerPhone
    FROM Quotes q
    LEFT JOIN Customers c ON c.CustomerID = q.CustomerID
    LEFT JOIN Users u ON u.User_ID = q.Created_By
    WHERE q.QuoteID = @QuoteID;
END
GO

-- עדכון תוצאות החיפוש השמורות של הצעה.
CREATE OR ALTER PROCEDURE sp_UpdateQuoteSearchResults
    @QuoteID INT,
    @SearchResultsJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Quotes
    SET SearchResultsJson = @SearchResultsJson
    WHERE QuoteID = @QuoteID;
END
GO

-- מזהי ההצעות שמכילות פריטים מספק נתון (למחיקה מדורגת של ספק).
CREATE OR ALTER PROCEDURE sp_GetQuoteIdsBySupplier
    @SupplierID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT QuoteID
    FROM QuoteItems
    WHERE SupplierID = @SupplierID;
END
GO

-- מזהי ההצעות שמכילות רכיב נתון (למחיקה מדורגת של רכיב).
CREATE OR ALTER PROCEDURE sp_GetQuoteIdsByComponent
    @ComponentSKU NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT QuoteID
    FROM QuoteItems
    WHERE ComponentSKU = @ComponentSKU;
END
GO

-- מזהי ההצעות של לקוח נתון (למחיקה מדורגת של לקוח).
CREATE OR ALTER PROCEDURE sp_GetQuoteIdsByCustomer
    @CustomerID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT QuoteID
    FROM Quotes
    WHERE CustomerID = @CustomerID;
END
GO

-- ===== Settings =====

-- כל ההגדרות.
CREATE OR ALTER PROCEDURE sp_GetAllSettings
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SettingKey, SettingValue
    FROM Settings;
END
GO

-- ערך של הגדרה בודדת.
CREATE OR ALTER PROCEDURE sp_GetSettingValue
    @SettingKey NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SettingValue
    FROM Settings
    WHERE SettingKey = @SettingKey;
END
GO

-- שמירת הגדרה: עדכון אם קיימת, הוספה אם לא (upsert).
CREATE OR ALTER PROCEDURE sp_SaveSetting
    @SettingKey NVARCHAR(100),
    @SettingValue NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM Settings WHERE SettingKey = @SettingKey)
        UPDATE Settings SET SettingValue = @SettingValue WHERE SettingKey = @SettingKey;
    ELSE
        INSERT INTO Settings (SettingKey, SettingValue) VALUES (@SettingKey, @SettingValue);
END
GO

-- ===== Components / Suppliers (חיפוש רכיב חלופי) =====

-- המק"ט החלופי של רכיב.
CREATE OR ALTER PROCEDURE sp_GetAlternativeSku
    @ComponentSKU NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT AlternativeSKU
    FROM Components
    WHERE ComponentSKU = @ComponentSKU;
END
GO

-- שם ספק לפי מזהה.
CREATE OR ALTER PROCEDURE sp_GetSupplierNameById
    @SupplierID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SupplierName
    FROM Suppliers
    WHERE SupplierID = @SupplierID;
END
GO
