import XCTest
@testable import AIClockBridge

// Hook events (POST /event) must come from this Mac and never from a web page.
final class HTTPServerSecurityTests: XCTestCase {
    func testLoopbackCurlAllowed() {
        XCTAssertTrue(HTTPServer.postAllowed(remoteIP: "127.0.0.1", headerLines: ["POST /event HTTP/1.1"]))
        XCTAssertTrue(HTTPServer.postAllowed(remoteIP: "::1", headerLines: ["POST /event HTTP/1.1"]))
        XCTAssertTrue(HTTPServer.postAllowed(remoteIP: "::ffff:127.0.0.1", headerLines: []))
    }

    func testLanRejected() {
        XCTAssertFalse(HTTPServer.postAllowed(remoteIP: "192.168.1.50", headerLines: ["POST /event HTTP/1.1"]))
        XCTAssertFalse(HTTPServer.postAllowed(remoteIP: "", headerLines: []))
    }

    func testBrowserOriginRejected() {
        XCTAssertFalse(HTTPServer.postAllowed(remoteIP: "127.0.0.1",
                                              headerLines: ["POST /event HTTP/1.1", "Origin: https://evil.example"]))
    }
}
