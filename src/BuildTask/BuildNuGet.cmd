md nuget
md nuget\build
copy CreateOmd.targets nuget\build\dotMorten.OmdGenerator.targets /Y
copy OmdGenerator.nuspec nuget\dotMorten.OmdGenerator.nuspec /Y
xcopy ..\Generator\bin\Release\PublishOutput\*.* nuget\generator\ /S /Y
Nuget pack nuget/dotMorten.OmdGenerator.nuspec

