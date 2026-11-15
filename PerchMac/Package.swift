// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Perch",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "Perch",
            path: "Sources/Perch",
            linkerSettings: [.linkedFramework("IOKit"), .linkedFramework("Carbon"), .linkedFramework("CoreAudio"),
                             .linkedFramework("Vision"), .linkedFramework("EventKit"), .linkedFramework("AVFoundation")]
        )
    ]
)
