IF OBJECT_ID('dbo.Flavors') IS NULL CREATE TABLE dbo.Flavors(Code CHAR(3) PRIMARY KEY, Name NVARCHAR(40) NOT NULL, Fragment NVARCHAR(40) NOT NULL);
GO
IF OBJECT_ID('dbo.Scans') IS NULL CREATE TABLE dbo.Scans(Id INT IDENTITY PRIMARY KEY, Operative NVARCHAR(30) NOT NULL, Code NVARCHAR(40) NOT NULL UNIQUE,
  FlavorCode CHAR(3) NOT NULL REFERENCES dbo.Flavors(Code), ScannedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Scans_Op') CREATE INDEX IX_Scans_Op ON dbo.Scans(Operative);
GO
IF OBJECT_ID('dbo.Orders') IS NULL CREATE TABLE dbo.Orders(Id INT IDENTITY PRIMARY KEY, Name NVARCHAR(60) NOT NULL, Email NVARCHAR(120) NOT NULL,
  Flavor CHAR(3) NOT NULL, Cases INT NOT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.Flavors) INSERT dbo.Flavors VALUES('ECL','Eclipse (black cherry)','SOMETHING'),('STA','Static (electric lime)','QUIET HAPPENS'),('GHO','Ghost (white peach)','AT MIDNIGHT');
GO
CREATE OR ALTER PROCEDURE dbo.sp_RecordScan @Op NVARCHAR(30), @Code NVARCHAR(40), @Flavor CHAR(3) AS
  INSERT dbo.Scans(Operative,Code,FlavorCode) VALUES(@Op,@Code,@Flavor);
GO
CREATE OR ALTER VIEW dbo.vw_Leaderboard AS
  SELECT Operative, COUNT(DISTINCT FlavorCode) AS Fragments, COUNT(*) AS Scans, MIN(ScannedAt) AS Since
  FROM dbo.Scans GROUP BY Operative;
