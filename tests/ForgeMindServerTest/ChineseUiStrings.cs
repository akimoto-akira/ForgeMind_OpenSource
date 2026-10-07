// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test;

/// <summary>
/// Captions of the editor's own UI controls, verbatim.
/// <para>The editor renders these dialogs in Chinese, so matching them requires the literal
/// text — and the rest of the source must stay Chinese-free. Every such literal lives in this
/// one file; exclude it from the non-Chinese source check rather than inlining the text at the
/// call sites.</para>
/// </summary>
public static class ChineseUiStrings
{
    /// <summary>Caption of the affirmative dialog button: "确定".</summary>
    public const string DialogOk = "确定";

    /// <summary>Caption of the confirm dialog button: "确认".</summary>
    public const string DialogConfirm = "确认";
}
