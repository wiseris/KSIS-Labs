Лабораторная 2
Tracert ICMP C#
Требуется добавить правило в cmd для корректной работы: netsh advfirewall firewall add rule name="All ICMP v4" dir=in action=allow protocol=icmpv4:any,any

Запуск производится в командной строке из папки с exe файлом D:\...\MyTracert\MyTracert\bin\Debug\net8.0>MyTracert.exe 77.88.44.55 или из папки с кодом Program.cs проекта с помощью dotnet run yandex.ru/ 77.88.44.55
