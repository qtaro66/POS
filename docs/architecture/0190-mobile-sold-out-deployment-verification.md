# Mobile Sold Out deployment verification

Use an isolated test Branch and Central with synthetic employees and menu items. Enable the runtime settings in [Branch-to-Central mTLS](0189-branch-central-mtls.md). Keep CA keys, client keys, credentials, and test evidence outside source control.

| Scenario | Action | Expected result |
| --- | --- | --- |
| Trusted Branch | Start Central with direct Kestrel HTTPS. Start Branch with its registered test certificate and a snapshot for that branch. | Branch accepts the first valid revision. Central returns only the authenticated branch's snapshot. |
| Untrusted client | Repeat with no certificate, an unregistered certificate, and one signed by an untrusted issuer. | The protected snapshot is unavailable to each client. |
| Branch isolation | Use a certificate registered for a second test branch and request the first branch through a query parameter. | The response contains only the second branch's data, or is unavailable if it has no snapshot. |
| Authorized Mobile | Pair a provisioned Mobile device, sign in as an active employee with `SetSoldOut`, and open Status. Set a known item by ID, then clear it with confirmation. | Both changes appear in the refreshed list and produce one audit entry each. |
| Read-only Mobile | Sign in on Mobile as an employee without the grant. | The list remains available and write controls are absent. The Branch API denies direct write requests. |
| Manual refresh | Publish a higher revision that removes the employee's grant. On PC POS Home, select the employee-permission refresh action. Refresh the Mobile list and try a request from any previously open view. | PC POS reports the update. Mobile no longer shows write controls, and the Branch API rejects a request using the old view. |
| Automatic refresh | Publish another higher revision. Let the next hourly poll complete. | Branch uses the new revision without a manual request. |
| Central outage | Make only Central unreachable. Keep Mobile connected to Branch over the LAN, then restart Branch. | Branch restores the last accepted permissions. A fresh Branch with no valid cache denies Mobile writes. |
| Certificate rotation | Register a replacement certificate, configure Branch to use it, then disable the old certificate registration. | The replacement can fetch permissions. Requests using the old registration are denied. |

Record the tested commit, deployment type, OS and browser versions, scenario outcome, and a sanitized evidence reference. Verify Android Chrome and iOS Safari separately. Automated coverage includes a simulated permission refresh and a loopback TLS handshake with a generated CA trusted only inside the test. Deployment and device results require the checks above.
