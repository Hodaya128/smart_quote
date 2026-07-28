/* =====================================================================
   Saved dashboard widgets (per user) — schema + stored procedures.
   הרץ פעם אחת על מסד הנתונים igroup35_prod.
   שומר גרפים/טבלאות שמשתמש שומר למסך הבית. ConfigJson = spec של הווידג'ט.
   ===================================================================== */

IF OBJECT_ID('dbo.SavedDashboardWidget', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SavedDashboardWidget (
        WidgetID    INT IDENTITY(1,1) PRIMARY KEY,
        UserID      INT            NOT NULL,
        Title       NVARCHAR(200)  NOT NULL,
        Type        NVARCHAR(20)   NOT NULL,   -- 'chart' | 'table'
        ConfigJson  NVARCHAR(MAX)  NOT NULL,
        SortOrder   INT            NOT NULL CONSTRAINT DF_Widget_Sort    DEFAULT(0),
        CreatedDate DATETIME       NOT NULL CONSTRAINT DF_Widget_Created DEFAULT(GETUTCDATE()),
        UpdatedDate DATETIME       NULL,
        CONSTRAINT FK_Widget_User FOREIGN KEY (UserID) REFERENCES dbo.Users(User_ID)
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetWidgetsByUserId
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT WidgetID, UserID, Title, Type, ConfigJson, SortOrder, CreatedDate, UpdatedDate
    FROM dbo.SavedDashboardWidget
    WHERE UserID = @UserID
    ORDER BY SortOrder, WidgetID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetWidgetById
    @WidgetID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT WidgetID, UserID, Title, Type, ConfigJson, SortOrder, CreatedDate, UpdatedDate
    FROM dbo.SavedDashboardWidget
    WHERE WidgetID = @WidgetID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_InsertWidget
    @UserID     INT,
    @Title      NVARCHAR(200),
    @Type       NVARCHAR(20),
    @ConfigJson NVARCHAR(MAX),
    @SortOrder  INT = 0,
    @NewID      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.SavedDashboardWidget (UserID, Title, Type, ConfigJson, SortOrder, CreatedDate)
    VALUES (@UserID, @Title, @Type, @ConfigJson, @SortOrder, GETUTCDATE());
    SET @NewID = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateWidget
    @WidgetID   INT,
    @Title      NVARCHAR(200),
    @Type       NVARCHAR(20),
    @ConfigJson NVARCHAR(MAX),
    @SortOrder  INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.SavedDashboardWidget
    SET Title = @Title, Type = @Type, ConfigJson = @ConfigJson,
        SortOrder = @SortOrder, UpdatedDate = GETUTCDATE()
    WHERE WidgetID = @WidgetID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DeleteWidget
    @WidgetID INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.SavedDashboardWidget WHERE WidgetID = @WidgetID;
END
GO
