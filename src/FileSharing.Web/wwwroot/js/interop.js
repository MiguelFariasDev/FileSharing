// Minimal JS interop surface for this app — clipboard access only, since Blazor Server has no
// built-in Clipboard API wrapper. Never touches localStorage/sessionStorage: the JWT never
// reaches this file or any other client-side script (see AuthTokenProvider).
window.fileSharingInterop = {
    copyToClipboard: function (text) {
        if (navigator.clipboard && window.isSecureContext) {
            return navigator.clipboard.writeText(text);
        }

        // Fallback for browsers/contexts without the async Clipboard API (e.g. plain http://
        // in local development).
        const textarea = document.createElement("textarea");
        textarea.value = text;
        textarea.style.position = "fixed";
        textarea.style.opacity = "0";
        document.body.appendChild(textarea);
        textarea.focus();
        textarea.select();

        try {
            if (!document.execCommand("copy")) {
                throw new Error("Copy command was not successful.");
            }
        } finally {
            document.body.removeChild(textarea);
        }
    }
};
