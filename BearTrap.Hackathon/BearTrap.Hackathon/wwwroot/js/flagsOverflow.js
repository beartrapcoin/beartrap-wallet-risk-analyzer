console.info("[flagsOverflow] loaded", new Date().toISOString());

window.flagsOverflow = window.flagsOverflow || {
    resizeHandler: null,
    resizeTimeoutId: null,
    resizeDebounceMs: 150,

    register() {
        if (!this.resizeHandler) {
            this.resizeHandler = () => this.scheduleRecalculateAll();
            window.addEventListener('resize', this.resizeHandler, { passive: true });
        }

        this.scheduleRecalculateAll();
    },

    scheduleRecalculateAll() {
        if (this.resizeTimeoutId) {
            clearTimeout(this.resizeTimeoutId);
        }

        this.resizeTimeoutId = window.setTimeout(() => {
            this.resizeTimeoutId = null;
            this.recalculateAll();
        }, this.resizeDebounceMs);
    },

    recalculateAll() {
        const containers = document.querySelectorAll('[data-risk-container]');
        console.debug('[flagsOverflow] containers:', containers.length);

        for (const container of containers) {
            this.recalculateContainer(container);
        }
    },

    recalculateContainer(container) {
        const riskRow = container.closest('.risk-row');
        const flagChips = Array.from(container.querySelectorAll('[data-risk-pill]'));
        const moreChip = riskRow?.querySelector('[data-risk-more]');

        this.hideMoreChip(moreChip);

        for (const chip of flagChips) {
            chip.classList.remove('is-hidden');
        }

        if (!moreChip || flagChips.length === 0) {
            console.debug('[flagsOverflow] card', {
                containerWidth: riskRow?.clientWidth ?? container.clientWidth,
                pillsCount: flagChips.length,
                hiddenCount: 0,
                moreWidth: 0
            });
            return;
        }

        const pillsComputed = window.getComputedStyle(container);
        const rowComputed = window.getComputedStyle(riskRow);
        const pillsGap = parseFloat(pillsComputed.columnGap || pillsComputed.gap || '8') || 8;
        const rowGap = parseFloat(rowComputed.columnGap || rowComputed.gap || '8') || 8;
        const containerWidth = riskRow.clientWidth;

        if (containerWidth === 0 && !container.dataset.riskRetryPending) {
            container.dataset.riskRetryPending = 'true';
            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    delete container.dataset.riskRetryPending;
                    this.recalculateContainer(container);
                });
            });
            return;
        }

        const widths = flagChips.map((chip) => Math.ceil(chip.getBoundingClientRect().width));

        const getPillsWidth = (count) => {
            if (count <= 0) {
                return 0;
            }

            let total = 0;
            for (let index = 0; index < count; index++) {
                total += widths[index];
            }

            if (count > 1) {
                total += pillsGap * (count - 1);
            }

            return total;
        };

        let visibleCount = flagChips.length;

        let hiddenCount = flagChips.length - visibleCount;
        let moreWidth = 0;

        while (visibleCount >= 0) {
            hiddenCount = flagChips.length - visibleCount;
            moreWidth = hiddenCount > 0 ? this.measureMoreChipWidth(moreChip, hiddenCount) : 0;

            const pillsWidth = getPillsWidth(visibleCount);
            const reserveForMore = hiddenCount > 0 ? moreWidth + (visibleCount > 0 ? rowGap : 0) : 0;
            const fits = pillsWidth + reserveForMore <= containerWidth + 0.5;

            if (fits) {
                break;
            }

            visibleCount--;
        }

        if (visibleCount < 0) {
            visibleCount = 0;
            hiddenCount = flagChips.length;
            moreWidth = this.measureMoreChipWidth(moreChip, hiddenCount);
        }

        for (let index = 0; index < flagChips.length; index++) {
            flagChips[index].classList.toggle('is-hidden', index >= visibleCount);
        }

        if (hiddenCount <= 0) {
            this.hideMoreChip(moreChip);
            console.debug('[flagsOverflow] card', {
                containerWidth,
                pillsCount: flagChips.length,
                hiddenCount: 0,
                moreWidth: 0
            });
            return;
        }

        const hiddenLabels = flagChips
            .slice(visibleCount)
            .map((chip) => chip.dataset.flagLabel || chip.textContent?.trim() || '')
            .filter((label) => label.length > 0);

        const tooltip = hiddenLabels.length > 0
            ? `Ukryte ryzyka: ${hiddenLabels.join(', ')}`
            : `Ukryte ryzyka: ${hiddenCount}`;

        moreChip.textContent = `+${hiddenCount}`;
        moreChip.title = tooltip;
        moreChip.setAttribute('aria-label', tooltip);
        moreChip.setAttribute('aria-hidden', 'false');
        moreChip.setAttribute('tabindex', '0');
        moreChip.style.display = 'inline-flex';
        moreChip.classList.add('is-visible');
        moreChip.classList.remove('is-hidden');

        console.debug('[flagsOverflow] card', {
            containerWidth,
            pillsCount: flagChips.length,
            hiddenCount,
            moreWidth
        });
    },

    measureMoreChipWidth(moreChip, hiddenCount) {
        const previousText = moreChip.textContent;
        const hadHiddenClass = moreChip.classList.contains('is-hidden');
        const previousVisibility = moreChip.style.visibility;
        const previousDisplay = moreChip.style.display;

        moreChip.textContent = `+${hiddenCount}`;
        moreChip.classList.remove('is-hidden');
        moreChip.style.display = 'inline-flex';
        moreChip.style.visibility = 'hidden';

        const width = Math.ceil(moreChip.getBoundingClientRect().width);

        moreChip.textContent = previousText;
        moreChip.style.display = previousDisplay;
        moreChip.style.visibility = previousVisibility;
        if (hadHiddenClass) {
            moreChip.classList.add('is-hidden');
        }

        return width;
    },

    hideMoreChip(moreChip) {
        if (!moreChip) {
            return;
        }

        moreChip.classList.remove('is-visible');
        moreChip.classList.add('is-hidden');
        moreChip.textContent = '';
        moreChip.title = '';
        moreChip.style.display = 'none';
        moreChip.setAttribute('aria-hidden', 'true');
        moreChip.setAttribute('tabindex', '-1');
    },

    dispose() {
        if (this.resizeHandler) {
            window.removeEventListener('resize', this.resizeHandler);
            this.resizeHandler = null;
        }

        if (this.resizeTimeoutId) {
            clearTimeout(this.resizeTimeoutId);
            this.resizeTimeoutId = null;
        }
    }
};

window.recalculateAllRiskFlags = () => window.flagsOverflow?.recalculateAll?.();
setTimeout(() => window.recalculateAllRiskFlags?.(), 0);

document.addEventListener('DOMContentLoaded', () => {
    window.recalculateAllRiskFlags?.();
});
