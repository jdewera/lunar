using Lunar.PortableExecutable;

namespace Lunar.Remote.Records;

internal record Module(nint Address, PEImage PEImage);