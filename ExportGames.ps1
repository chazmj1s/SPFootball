$server = "five-heart-svr.database.windows.net"
$database = "five-heart-db"
$username = "FiveHeart"
$password = "Bird!@nd15H"

$outputFile = "C:\Temp\Games.csv"

bcp "EXEC dbo.ExportGamesCsv" queryout $outputFile -d $database -c -t"," -S $server -U $username -P $password

Write-Host "Export complete: $outputFile"	