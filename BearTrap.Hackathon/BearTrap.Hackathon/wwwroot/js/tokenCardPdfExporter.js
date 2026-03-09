(function () {
    const A4_WIDTH_MM = 210;
    const A4_HEIGHT_MM = 297;
    const PAGE_MARGIN_MM = 8;
    const CARD_GAP_MM = 6;
    const CARD_INTERACTIVE_SELECTOR = [
        "a",
        "button",
        "input",
        "select",
        "textarea",
        "label",
        "[role='button']",
        "[data-risk-pill]",
        "[data-risk-more]",
        "[data-risk-modal-close]",
        "[data-no-card-select]"
    ].join(",");

    let selectionClickHandler = null;

    function clampColorChannel(value) {
        return Math.max(0, Math.min(255, Math.round(value)));
    }

    function parseHexColorString(value) {
        const hex = (value || "").trim();
        if (!hex.startsWith("#")) {
            return null;
        }

        const raw = hex.slice(1);
        if (![3, 4, 6, 8].includes(raw.length)) {
            return null;
        }

        if (raw.length === 3 || raw.length === 4) {
            const r = Number.parseInt(raw[0] + raw[0], 16);
            const g = Number.parseInt(raw[1] + raw[1], 16);
            const b = Number.parseInt(raw[2] + raw[2], 16);
            const a = raw.length === 4 ? Number.parseInt(raw[3] + raw[3], 16) / 255 : 1;

            return [r, g, b, a].some(Number.isNaN)
                ? null
                : { r, g, b, a };
        }

        const r = Number.parseInt(raw.slice(0, 2), 16);
        const g = Number.parseInt(raw.slice(2, 4), 16);
        const b = Number.parseInt(raw.slice(4, 6), 16);
        const a = raw.length === 8 ? Number.parseInt(raw.slice(6, 8), 16) / 255 : 1;

        return [r, g, b, a].some(Number.isNaN)
            ? null
            : { r, g, b, a };
    }

    function parseCssColorToRgba(value) {
        if (!value || typeof value !== "string") {
            return null;
        }

        const color = value.trim().toLowerCase();
        if (!color || color === "transparent") {
            return { r: 0, g: 0, b: 0, a: 0 };
        }

        const fromHex = parseHexColorString(color);
        if (fromHex) {
            return fromHex;
        }

        const rgbMatch = color.match(/^rgba?\(([^)]+)\)$/);
        if (!rgbMatch) {
            return null;
        }

        const parts = rgbMatch[1].split(",").map((part) => part.trim());
        if (parts.length !== 3 && parts.length !== 4) {
            return null;
        }

        const r = Number.parseFloat(parts[0]);
        const g = Number.parseFloat(parts[1]);
        const b = Number.parseFloat(parts[2]);
        const a = parts.length === 4 ? Number.parseFloat(parts[3]) : 1;

        if ([r, g, b, a].some(Number.isNaN)) {
            return null;
        }

        return {
            r: clampColorChannel(r),
            g: clampColorChannel(g),
            b: clampColorChannel(b),
            a: Math.max(0, Math.min(1, a))
        };
    }

    function blendRgbaOverRgb(foreground, background) {
        const alpha = Math.max(0, Math.min(1, foreground.a));
        return {
            r: clampColorChannel((foreground.r * alpha) + (background.r * (1 - alpha))),
            g: clampColorChannel((foreground.g * alpha) + (background.g * (1 - alpha))),
            b: clampColorChannel((foreground.b * alpha) + (background.b * (1 - alpha)))
        };
    }

    function rgbToCssString(rgb) {
        return `rgb(${rgb.r}, ${rgb.g}, ${rgb.b})`;
    }

    function resolveFallbackBackgroundRgb() {
        const rootStyle = window.getComputedStyle(document.documentElement);
        const varColor = rootStyle.getPropertyValue("--bt-parch-0");
        const parsed = parseCssColorToRgba(varColor);

        if (parsed && parsed.a > 0) {
            return {
                r: parsed.r,
                g: parsed.g,
                b: parsed.b
            };
        }

        return { r: 243, g: 234, b: 215 };
    }

    function resolvePdfBackgroundContext(sourceElement) {
        const fallback = resolveFallbackBackgroundRgb();
        const candidates = [];

        if (sourceElement && typeof sourceElement.closest === "function") {
            const tokensContent = sourceElement.closest(".tokens-content");
            const mainPanel = sourceElement.closest(".main-panel");
            const pageWrapper = sourceElement.closest(".page-wrapper");

            if (tokensContent) {
                candidates.push({ element: tokensContent, label: ".tokens-content" });
            }

            if (mainPanel) {
                candidates.push({ element: mainPanel, label: ".main-panel" });
            }

            if (pageWrapper) {
                candidates.push({ element: pageWrapper, label: ".page-wrapper" });
            }
        }

        if (document.body) {
            candidates.push({ element: document.body, label: "body" });
        }

        for (const candidate of candidates) {
            const rawColor = window.getComputedStyle(candidate.element).backgroundColor;
            const parsed = parseCssColorToRgba(rawColor);
            if (!parsed || parsed.a <= 0) {
                continue;
            }

            const rgb = parsed.a < 1
                ? blendRgbaOverRgb(parsed, fallback)
                : { r: parsed.r, g: parsed.g, b: parsed.b };

            return {
                sourceLabel: candidate.label,
                sourceColor: rawColor,
                resolvedRgb: rgb,
                canvasBackgroundColor: rgbToCssString(rgb)
            };
        }

        return {
            sourceLabel: "fallback(--bt-parch-0)",
            sourceColor: "n/a",
            resolvedRgb: fallback,
            canvasBackgroundColor: rgbToCssString(fallback)
        };
    }

    function resolveJsPdfConstructor() {
        if (window.jspdf && window.jspdf.jsPDF) {
            return window.jspdf.jsPDF;
        }

        if (window.jsPDF) {
            return window.jsPDF;
        }

        return null;
    }

    function toAbsoluteUrl(rawUrl) {
        if (!rawUrl || typeof rawUrl !== "string") {
            return null;
        }

        try {
            return new URL(rawUrl, window.location.href).href;
        }
        catch {
            return null;
        }
    }

    function toProxiedImageUrl(rawUrl) {
        const absoluteUrl = toAbsoluteUrl(rawUrl);
        if (!absoluteUrl) {
            return null;
        }

        if (absoluteUrl.startsWith("data:") || absoluteUrl.startsWith("blob:")) {
            return absoluteUrl;
        }

        const parsed = new URL(absoluteUrl);
        if (parsed.origin === window.location.origin) {
            return absoluteUrl;
        }

        return `/api/image-proxy?url=${encodeURIComponent(absoluteUrl)}`;
    }

    function fileToDataUrl(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result);
            reader.onerror = () => reject(reader.error || new Error("Failed to read file."));
            reader.readAsDataURL(file);
        });
    }

    async function fetchImageViaProxy(proxyUrl) {
        if (!proxyUrl) {
            return null;
        }

        try {
            const response = await fetch(proxyUrl, {
                method: "GET",
                credentials: "same-origin",
                cache: "no-store"
            });

            if (!response.ok) {
                return null;
            }

            const blob = await response.blob();
            if (!blob || blob.size === 0) {
                return null;
            }

            const blobUrl = URL.createObjectURL(blob);
            const dataUrl = await fileToDataUrl(blob);

            return {
                blobUrl,
                dataUrl
            };
        }
        catch {
            return null;
        }
    }

    async function buildImageSourcePlan(element) {
        const plan = new Map();
        const images = element.querySelectorAll("img[src]");

        for (const img of images) {
            const originalSrc = img.getAttribute("src");
            if (!originalSrc || plan.has(originalSrc)) {
                continue;
            }

            const directUrl = toAbsoluteUrl(originalSrc);
            const proxyUrl = toProxiedImageUrl(originalSrc);
            const proxiedBinary = await fetchImageViaProxy(proxyUrl);

            plan.set(originalSrc, {
                directUrl,
                proxyUrl,
                proxyBlobUrl: proxiedBinary?.blobUrl || null,
                proxyDataUrl: proxiedBinary?.dataUrl || null
            });
        }

        return plan;
    }

    function resolveImageVariant(sourceEntry, imageMode) {
        if (!sourceEntry) {
            return null;
        }

        switch (imageMode) {
            case "proxy-data-url":
                return sourceEntry.proxyDataUrl || sourceEntry.proxyBlobUrl || sourceEntry.proxyUrl || sourceEntry.directUrl;
            case "proxy-blob-url":
                return sourceEntry.proxyBlobUrl || sourceEntry.proxyDataUrl || sourceEntry.proxyUrl || sourceEntry.directUrl;
            case "proxy-url":
                return sourceEntry.proxyUrl || sourceEntry.directUrl;
            case "direct-url":
                return sourceEntry.directUrl;
            default:
                return sourceEntry.proxyUrl || sourceEntry.directUrl;
        }
    }

    function rewriteImagesInClone(clonedDoc, cardSelector, sourcePlan, imageMode) {
        const clonedCard = clonedDoc.querySelector(cardSelector);
        if (!clonedCard) {
            return;
        }

        // Selection border is a GUI-only state and should not be visible in exported PDF.
        clonedCard.classList.remove("is-selected");

        const images = clonedCard.querySelectorAll("img");
        images.forEach((img) => {
            const originalSrc = img.getAttribute("src");
            if (!originalSrc) {
                return;
            }

            const sourceEntry = sourcePlan.get(originalSrc);
            const updatedSrc = resolveImageVariant(sourceEntry, imageMode);

            if (!updatedSrc) {
                return;
            }

            img.setAttribute("src", updatedSrc);
            img.removeAttribute("srcset");
            img.setAttribute("crossorigin", "anonymous");
            img.setAttribute("loading", "eager");
            img.style.visibility = "visible";
            img.style.opacity = "1";
        });
    }

    async function renderCardCanvas(element, cardSelector, includeImages, sourcePlan, imageMode, canvasBackgroundColor) {
        return window.html2canvas(element, {
            backgroundColor: canvasBackgroundColor,
            scale: Math.max(1.5, Math.min(2, window.devicePixelRatio || 1.5)),
            useCORS: true,
            allowTaint: false,
            logging: false,
            imageTimeout: 12000,
            onclone: (clonedDoc) => {
                if (!includeImages) {
                    return;
                }

                rewriteImagesInClone(clonedDoc, cardSelector, sourcePlan, imageMode);
            },
            ignoreElements: includeImages
                ? undefined
                : (node) => node && node.tagName === "IMG"
        });
    }

    function disposeImageSourcePlan(sourcePlan) {
        for (const entry of sourcePlan.values()) {
            if (entry.proxyBlobUrl) {
                URL.revokeObjectURL(entry.proxyBlobUrl);
            }
        }
    }

    async function captureCanvasWithFallback(element, cardSelector, canvasBackgroundColor) {
        const sourcePlan = await buildImageSourcePlan(element);

        const attempts = [
            { includeImages: true, imageMode: "proxy-data-url", label: "proxy-data-url" },
            { includeImages: true, imageMode: "proxy-blob-url", label: "proxy-blob-url" },
            { includeImages: true, imageMode: "proxy-url", label: "proxy-url" },
            { includeImages: true, imageMode: "direct-url", label: "direct-url" },
            { includeImages: false, imageMode: "none", label: "no-images" }
        ];

        let lastError;
        try {
            for (const attempt of attempts) {
                try {
                    const canvas = await renderCardCanvas(
                        element,
                        cardSelector,
                        attempt.includeImages,
                        sourcePlan,
                        attempt.imageMode,
                        canvasBackgroundColor
                    );

                    // Validate that canvas can be serialized before building the PDF.
                    canvas.toDataURL("image/png", 1.0);
                    console.info(`[token-pdf] Capture strategy succeeded: ${attempt.label}`);
                    return canvas;
                }
                catch (error) {
                    lastError = error;
                    console.warn(`[token-pdf] Capture attempt failed (${attempt.label}).`, error);
                }
            }
        }
        finally {
            disposeImageSourcePlan(sourcePlan);
        }

        throw new Error(`Unable to capture token card: ${lastError}`);
    }

    function isCardToggleEligible(target) {
        if (!target || typeof target.closest !== "function") {
            return false;
        }

        return !target.closest(CARD_INTERACTIVE_SELECTOR);
    }

    function disposeSelection() {
        if (selectionClickHandler) {
            document.removeEventListener("click", selectionClickHandler, false);
            selectionClickHandler = null;
        }
    }

    function registerCardSelection(dotNetRef) {
        disposeSelection();
        if (!dotNetRef) {
            return;
        }

        selectionClickHandler = (event) => {
            const card = event.target.closest(".tcard[data-token-card='true']");
            if (!card) {
                return;
            }

            if (!isCardToggleEligible(event.target)) {
                return;
            }

            const tokenKey = card.getAttribute("data-token-key");
            if (!tokenKey) {
                return;
            }

            dotNetRef.invokeMethodAsync("ToggleTokenCardSelection", tokenKey)
                .catch((error) => console.error("[token-pdf] Card selection toggle failed.", error));
        };

        document.addEventListener("click", selectionClickHandler, false);
    }

    function ensureSafeFilename(fileName) {
        const fallback = "token-export.pdf";
        if (!fileName || typeof fileName !== "string") {
            return fallback;
        }

        const trimmed = fileName.trim();
        if (!trimmed) {
            return fallback;
        }

        return trimmed.toLowerCase().endsWith(".pdf")
            ? trimmed
            : `${trimmed}.pdf`;
    }

    function calculateCanvasRenderSize(canvas, maxWidth, maxHeight) {
        const imageRatio = canvas.width / canvas.height;
        let targetWidth = maxWidth;
        let targetHeight = targetWidth / imageRatio;

        if (targetHeight > maxHeight) {
            targetHeight = maxHeight;
            targetWidth = targetHeight * imageRatio;
        }

        return {
            width: targetWidth,
            height: targetHeight
        };
    }

    function fillPdfPageBackground(pdf, rgbColor) {
        pdf.setFillColor(rgbColor.r, rgbColor.g, rgbColor.b);
        pdf.rect(0, 0, A4_WIDTH_MM, A4_HEIGHT_MM, "F");
    }

    function addCanvasToPdf(pdf, canvas, pdfBackgroundRgb) {
        const maxWidth = A4_WIDTH_MM - (PAGE_MARGIN_MM * 2);
        const maxHeight = A4_HEIGHT_MM - (PAGE_MARGIN_MM * 2);

        const renderSize = calculateCanvasRenderSize(canvas, maxWidth, maxHeight);

        const offsetX = (A4_WIDTH_MM - renderSize.width) / 2;
        const offsetY = (A4_HEIGHT_MM - renderSize.height) / 2;

        fillPdfPageBackground(pdf, pdfBackgroundRgb);

        let imageData;
        try {
            imageData = canvas.toDataURL("image/png", 1.0);
        }
        catch (error) {
            throw new Error(`Canvas serialization failed: ${error}`);
        }

        pdf.addImage(imageData, "PNG", offsetX, offsetY, renderSize.width, renderSize.height, undefined, "FAST");
    }

    async function exportCardToPdf(cardSelector, fileName) {
        if (!window.html2canvas) {
            throw new Error("html2canvas is not loaded.");
        }

        const JsPdf = resolveJsPdfConstructor();
        if (!JsPdf) {
            throw new Error("jsPDF is not loaded.");
        }

        if (!cardSelector || typeof cardSelector !== "string") {
            throw new Error("Missing token card selector.");
        }

        const element = document.querySelector(cardSelector);
        if (!element) {
            throw new Error(`Token card not found for selector: ${cardSelector}`);
        }

        const backgroundContext = resolvePdfBackgroundContext(element);
        console.info("[token-pdf] PDF background context", backgroundContext);

        const canvas = await captureCanvasWithFallback(element, cardSelector, backgroundContext.canvasBackgroundColor);

        const pdf = new JsPdf({
            orientation: "p",
            unit: "mm",
            format: "a4",
            compress: true
        });

        addCanvasToPdf(pdf, canvas, backgroundContext.resolvedRgb);
        pdf.save(ensureSafeFilename(fileName));
    }

    async function exportSelectedTokenCardsToPdf(cardSelectors, fileName, tokenMetadataList) {
        if (!Array.isArray(cardSelectors) || cardSelectors.length === 0) {
            throw new Error("No selected token cards to export.");
        }

        if (!window.html2canvas) {
            throw new Error("html2canvas is not loaded.");
        }

        const JsPdf = resolveJsPdfConstructor();
        if (!JsPdf) {
            throw new Error("jsPDF is not loaded.");
        }

        console.info("[token-pdf] Selected card IDs/selectors:", cardSelectors);

        const firstAvailableElement = cardSelectors
            .map((selector) => document.querySelector(selector))
            .find((element) => !!element);

        const backgroundContext = resolvePdfBackgroundContext(firstAvailableElement || document.body);
        console.info("[token-pdf] PDF background context", backgroundContext);

        const pdf = new JsPdf({
            orientation: "p",
            unit: "mm",
            format: "a4",
            compress: true
        });

        const leftMargin = PAGE_MARGIN_MM;
        const rightMargin = PAGE_MARGIN_MM;
        const topMargin = PAGE_MARGIN_MM;
        const bottomMargin = PAGE_MARGIN_MM;
        const maxRenderWidth = A4_WIDTH_MM - leftMargin - rightMargin;
        const maxRenderHeight = A4_HEIGHT_MM - topMargin - bottomMargin;
        const pageBottomY = A4_HEIGHT_MM - bottomMargin;

        let exportedCards = 0;
        let pageNumber = 1;
        let currentY = topMargin;

        console.info(`[token-pdf] Total selected cards to export: ${cardSelectors.length}`);
        fillPdfPageBackground(pdf, backgroundContext.resolvedRgb);

        for (let i = 0; i < cardSelectors.length; i += 1) {
            const selector = cardSelectors[i];
            const element = document.querySelector(selector);
            if (!element) {
                console.error(`[token-pdf] Skipping missing card for selector: ${selector}`);
                continue;
            }

            const tokenKey = (element.getAttribute("data-token-key") || "").trim();
            const metadata = Array.isArray(tokenMetadataList) ? tokenMetadataList[i] : null;
            const tokenName = metadata?.Name || "";
            const tokenSymbol = metadata?.Symbol || "";
            const tokenAddress = (metadata?.Address || tokenKey || "").trim();

            console.info(`[token-pdf] Exporting card ${i + 1}/${cardSelectors.length}`, {
                selector,
                tokenName,
                tokenSymbol,
                tokenAddress
            });

            try {
                const canvas = await captureCanvasWithFallback(element, selector, backgroundContext.canvasBackgroundColor);

                const renderSize = calculateCanvasRenderSize(canvas, maxRenderWidth, maxRenderHeight);

                console.info(`[token-pdf] Card ${i + 1} canvas dimensions`, {
                    canvasWidth: canvas.width,
                    canvasHeight: canvas.height,
                    renderWidth: renderSize.width,
                    renderHeight: renderSize.height,
                    pageNumber,
                    currentYBefore: currentY
                });

                if (currentY + renderSize.height > pageBottomY) {
                    console.info(`[token-pdf] Page break before card ${i + 1}`, {
                        pageNumberBefore: pageNumber,
                        currentY,
                        requiredHeight: renderSize.height,
                        pageBottomY
                    });

                    pdf.addPage();
                    pageNumber += 1;
                    fillPdfPageBackground(pdf, backgroundContext.resolvedRgb);
                    currentY = topMargin;
                }

                const x = (A4_WIDTH_MM - renderSize.width) / 2;
                const imageData = canvas.toDataURL("image/png", 1.0);
                pdf.addImage(imageData, "PNG", x, currentY, renderSize.width, renderSize.height, undefined, "FAST");

                const isValidTokenAddress = /^0x[a-fA-F0-9]{40}$/.test(tokenAddress);
                if (isValidTokenAddress) {
                    const fourMemeUrl = `https://four.meme/token/${tokenAddress}`;
                    pdf.link(x, currentY, renderSize.width, renderSize.height, { url: fourMemeUrl });
                    console.info("[token-pdf] Added clickable Four.Meme link to PDF card", {
                        tokenName,
                        tokenAddress,
                        fourMemeUrl,
                        linked: true
                    });
                }

                exportedCards += 1;
                const yAfter = currentY + renderSize.height + CARD_GAP_MM;

                console.info(`[token-pdf] Added card ${i + 1} to PDF`, {
                    pageNumber,
                    yPlaced: currentY,
                    yAfter,
                    tokenName: tokenName || tokenSymbol || tokenAddress
                });

                currentY = yAfter;
            }
            catch (error) {
                console.error(`[token-pdf] Export failed for selector: ${selector}`, {
                    tokenName,
                    tokenSymbol,
                    tokenAddress,
                    error
                });
            }
        }

        if (exportedCards === 0) {
            throw new Error("No selected token card could be exported.");
        }

        pdf.save(ensureSafeFilename(fileName || "selected-tokens.pdf"));
    }

    window.tokenCardPdfExporter = {
        exportCardToPdf,
        exportSelectedTokenCardsToPdf,
        registerCardSelection,
        disposeSelection
    };
})();
