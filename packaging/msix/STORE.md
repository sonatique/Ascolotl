# Publishing Ascolotl on the Microsoft Store

From zero to a published Store listing. `AppxManifest.xml` in this folder
declares the .adoc/.asciidoc file association and full-trust desktop mode;
`build-msix.ps1` builds the x64 and arm64 packages and bundles them.

## Phase 1: Developer account (once)

1. Start at https://storedeveloper.microsoft.com and choose **Get started**.
   The older Partner Center registration page
   (partner.microsoft.com/dashboard/registration) shows "Access restricted"
   when signed in with a work or school account.
2. Choose **Individual developer**. Individual and Company accounts were both
   free at the time of writing; a Company account needs a business
   verification that can take days.
3. Sign in with a **personal** Microsoft account: the one that should own the
   app long term.
4. Fill in name, country and contact email, and choose a **publisher display
   name**. The Store shows it as the publisher on the app's page.
5. Complete the identity check (ID document and selfie), then wait for the
   confirmation email: a few hours to a few days.

## Phase 2: Reserve the app (once)

6. In Partner Center: **Apps and games > New product > MSIX or PWA app**.
7. Reserve the name **Ascolotl** (or an alternative if it is taken).
8. Open the app, then **Product management > Product identity**, and note:
   - `Package/Identity/Name`, for example `12345Publisher.Ascolotl`
   - `Package/Identity/Publisher`, for example `CN=XXXXXXXX-XXXX-...`
   - `Package/Properties/PublisherDisplayName`

## Phase 3: Build and test the package

9. Install the Windows SDK once, which provides `makeappx.exe`:

   ```
   winget install Microsoft.WindowsSDK.10.0.26100
   ```

10. Build the Store bundle with the identity values. The version must look
    like `1.0.0.0`: the first number cannot be 0 and the last must be 0.

    ```
    powershell -File packaging/msix/build-msix.ps1 -PackageVersion 1.0.0.0 -IdentityName "<Name>" -Publisher "<CN=...>" -PublisherDisplayName "<display name>"
    ```

    Output: `publish/msix/Ascolotl_1.0.0.0.msixbundle`. The Store signs the
    package; no certificate is needed for submission.
11. Test locally. Turn on **Settings > System > For developers > Developer
    Mode**, install the unpacked package with `Add-AppxPackage -Register`, and
    check:
    - the app starts from the Start menu;
    - double-clicking an .adoc file opens it in Ascolotl;
    - settings and recovery work (a packaged app may keep its data elsewhere).
12. Optionally run the Windows App Certification Kit (in the SDK) on the
    bundle to catch problems before Microsoft's review does.

## Phase 4: Store listing material

13. Prepare:
    - **Description** (up to 10,000 characters) and a short description.
    - **Keywords:** up to 7, for example AsciiDoc, editor, markup,
      documentation, PDF, writer, technical writing.
    - **Screenshots:** at least 1, ideally 4-6, at least 1366x768. Light and
      dark theme, preview, PDF export.
    - **Privacy policy page:** a short page stating the app collects no data.
      It avoids review questions.
    - **Release notes** for 1.0.0.
14. Decide price, markets and category (**Productivity** or **Developer
    tools**).

## Phase 5: Submit

15. In Partner Center choose **Start your submission** and fill in:
    - **Pricing and availability:** price, markets, public visibility.
    - **Properties:** category, privacy policy URL, support contact (email or
      GitHub issues URL).
    - **Age ratings:** the IARC questionnaire. Answer no to everything; the
      result should be "3+" or "Everyone".
    - **Packages:** upload the `.msixbundle`. Partner Center checks it at
      once; identity mismatches show up here.
    - **Store listings (English):** description, screenshots, keywords,
      release notes.
    - **Submission options > restricted capability `runFullTrust`:** justify
      it, for example: "Ascolotl is a desktop application built with
      .NET/Avalonia; it needs full trust to read and write the user's AsciiDoc
      files and folders and export PDF/Word/HTML to locations the user
      chooses."
16. Click **Submit to the Store**. Certification usually takes 1-3 business
    days. A rejection comes with a report saying why.

## Phase 6: Updates

17. Bump the version (`1.0.1.0`, and so on), rebuild the bundle, then in
    Partner Center choose **Update**, replace the package, edit the release
    notes and submit.
