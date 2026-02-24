# NetCoreGenericHost.Playground
How ignite/shutdown NetCore host applications

I created this repo in order to learn all the stuff included behind the scenes that help developers to create Console, Web and Service apps in a proper way.

The below points are interesting:

 - .Net projects SDK [Microsoft Docs](https://docs.microsoft.com/en-us/dotnet/core/project-sdk/overview)
 Each project SDK is a set of MSBuild targets and associated tasks that are responsible for compiling, packing, and publishing code. 
 Available SDKs: Microsoft.NET.Sdk, Microsoft.NET.Sdk.Web, Microsoft.NET.Sdk.Worker, etc

 ```xml
<Project Sdk="Microsoft.NET.Sdk">
  ...
</Project>
```

- .Net Generic Host [Microsoft Docs](https://docs.microsoft.com/en-us/dotnet/core/extensions/generic-host)
    A host is an object that encapsulates an app's resources, such as:
	 - Dependency injection (DI)
	- Logging
	- Configuration
	- IHostedService implementations
 
	The main reason for including all of the app's interdependent resources in one object is lifetime management: control over app startup and graceful shutdown. This is achieved with the Microsoft.Extensions.Hosting NuGet package.

- Running Console, WebApps share the same DI container
