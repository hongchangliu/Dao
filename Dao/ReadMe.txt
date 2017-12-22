1、Dao.exe.config需要配置server，user id，password，database
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <add key="ConnectionString" value="server=localhost;user id=root;password=root;database=test"/>
	<add key="sql" value="需要查询的sql语句"/>
  </appSettings>
</configuration>