# DotNetOMDGenerator
A tool that generates an object model diagram of a set of C# source files.

### Usage:
```
dotnet GENERATOR.dll /source=[source folder] /format=[html|image] /ShowPrivate /ShowInternal
  source        Specifies the folder of source files to include for the object model.
                Separate with | for multiple folders
  format        Format to generate: 'image' generates an image for each object.
                'html' a single html output (html is default)
  ShowPrivate   Show private members (default is false)
  ShowInternal  Show internal members (default is false)
```

An example of a generated output for all of .NET Core can be found [here](http://www.sharpgis.net/Tests/corefx.html).