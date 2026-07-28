/* =====================================================================
   Supplier login credentials — schema + stored procedures
   הרץ פעם אחת על מסד הנתונים igroup35_prod.
   הסיסמה (LoginPassword) נשמרת מוצפנת (Base64 של AES) ע"י השרת,
   ולכן NVARCHAR(MAX) ולא טקסט גלוי.
   ===================================================================== */

-- 1) הוספת עמודות לטבלת Suppliers (רק אם לא קיימות) ----------------------
IF COL_LENGTH('dbo.Suppliers', 'RequiresLogin') IS NULL
    ALTER TABLE dbo.Suppliers ADD RequiresLogin BIT NOT NULL CONSTRAINT DF_Suppliers_RequiresLogin DEFAULT(0);

IF COL_LENGTH('dbo.Suppliers', 'LoginAccount') IS NULL
    ALTER TABLE dbo.Suppliers ADD LoginAccount NVARCHAR(100) NULL;

IF COL_LENGTH('dbo.Suppliers', 'LoginUsername') IS NULL
    ALTER TABLE dbo.Suppliers ADD LoginUsername NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.Suppliers', 'LoginPassword') IS NULL
    ALTER TABLE dbo.Suppliers ADD LoginPassword NVARCHAR(MAX) NULL;
GO

-- 2) sp_GetAllSuppliers --------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_GetAllSuppliers
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SupplierID, SupplierName, WebsiteURL,
           RequiresLogin, LoginAccount, LoginUsername, LoginPassword
    FROM dbo.Suppliers
    ORDER BY SupplierName;
END
GO

-- 3) sp_GetSupplierById --------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_GetSupplierById
    @SupplierID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SupplierID, SupplierName, WebsiteURL,
           RequiresLogin, LoginAccount, LoginUsername, LoginPassword
    FROM dbo.Suppliers
    WHERE SupplierID = @SupplierID;
END
GO

-- 4) sp_InsertSupplier ---------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_InsertSupplier
    @SupplierName  NVARCHAR(200),
    @WebsiteURL    NVARCHAR(500)  = NULL,
    @RequiresLogin BIT            = 0,
    @LoginAccount  NVARCHAR(100)  = NULL,
    @LoginUsername NVARCHAR(200)  = NULL,
    @LoginPassword NVARCHAR(MAX)  = NULL,
    @NewID         INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Suppliers
        (SupplierName, WebsiteURL, RequiresLogin, LoginAccount, LoginUsername, LoginPassword)
    VALUES
        (@SupplierName, @WebsiteURL, @RequiresLogin, @LoginAccount, @LoginUsername, @LoginPassword);

    SET @NewID = SCOPE_IDENTITY();
END
GO

-- 5) sp_UpdateSupplier ---------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_UpdateSupplier
    @SupplierID    INT,
    @SupplierName  NVARCHAR(200),
    @WebsiteURL    NVARCHAR(500)  = NULL,
    @RequiresLogin BIT            = 0,
    @LoginAccount  NVARCHAR(100)  = NULL,
    @LoginUsername NVARCHAR(200)  = NULL,
    @LoginPassword NVARCHAR(MAX)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Suppliers
    SET SupplierName  = @SupplierName,
        WebsiteURL    = @WebsiteURL,
        RequiresLogin = @RequiresLogin,
        LoginAccount  = @LoginAccount,
        LoginUsername = @LoginUsername,
        LoginPassword = @LoginPassword
    WHERE SupplierID = @SupplierID;
END
GO
