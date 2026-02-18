window.flagsOverflow = window.flagsOverflow || {
    dotNetRef: null,
    resizeHandler: null,
    resizeRafId: null,

    register(dotNetRef) {
        this.dotNetRef = dotNetRef;

        if (!this.resizeHandler) {
            this.resizeHandler = () => this.scheduleRecalculateAll();
            window.addEventListener('resize', this.resizeHandler, { passive: true });
        }

        this.scheduleRecalculateAll();
    },

    scheduleRecalculateAll() {
        if (this.resizeRafId) {
            cancelAnimationFrame(this.resizeRafId);
        }

        this.resizeRafId = requestAnimationFrame(() => {
            this.resizeRafId = null;
            this.recalculateAll();
        });
    },

    recalculateAll() {
        const containers = document.querySelectorAll('.tcard-bottom[data-flags-token-key]');
        const states = [];

        for (const container of containers) {
            const state = this.recalculateContainer(container);
            if (state) {
                states.push(state);
            }
        }

        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('UpdateFlagsOverflowBatch', states);
        }
    },

    recalculateContainer(container) {
        const tokenKey = container.dataset.flagsTokenKey;
        if (!tokenKey) {
            return null;
        }

        const flagChips = Array.from(container.querySelectorAll('.flag-chip'));
        if (flagChips.length === 0) {
            return { tokenKey, visibleCount: 0 };
        }

        const computed = window.getComputedStyle(container);
        const gap = parseFloat(computed.columnGap || computed.gap || '0') || 0;
        const containerWidth = container.clientWidth;

        for (const chip of flagChips) {
            chip.hidden = false;
            chip.style.display = '';
        }

        const widths = flagChips.map((chip) => Math.ceil(chip.getBoundingClientRect().width));

        let visibleCount = flagChips.length;

        while (visibleCount >= 0) {
            const hiddenCount = flagChips.length - visibleCount;

            let usedWidth = 0;
            for (let index = 0; index < visibleCount; index++) {
                usedWidth += widths[index];
            }

            if (visibleCount > 1) {
                usedWidth += gap * (visibleCount - 1);
            }

            if (hiddenCount > 0) {
                const moreChipWidth = this.measureMoreChipWidth(container, hiddenCount);
                usedWidth += moreChipWidth;

                if (visibleCount > 0) {
                    usedWidth += gap;
                }
            }

            if (usedWidth <= containerWidth + 0.5) {
                break;
            }

            visibleCount--;
        }

        if (visibleCount < 0) {
            visibleCount = 0;
        }

        for (let index = 0; index < flagChips.length; index++) {
            const isVisible = index < visibleCount;
            const chip = flagChips[index];
            chip.hidden = !isVisible;
            chip.style.display = isVisible ? '' : 'none';
        }

        return { tokenKey, visibleCount };
    },

    measureMoreChipWidth(container, hiddenCount) {
        const tempButton = document.createElement('button');
        tempButton.type = 'button';
        tempButton.className = 'tchip tchip-more tchip-button more-chip';
        tempButton.textContent = `+${hiddenCount}`;
        tempButton.style.position = 'absolute';
        tempButton.style.visibility = 'hidden';
        tempButton.style.pointerEvents = 'none';
        tempButton.style.left = '-9999px';
        tempButton.style.top = '0';

        container.appendChild(tempButton);
        const width = Math.ceil(tempButton.getBoundingClientRect().width);
        container.removeChild(tempButton);

        return width;
    },

    dispose() {
        if (this.resizeHandler) {
            window.removeEventListener('resize', this.resizeHandler);
            this.resizeHandler = null;
        }

        if (this.resizeRafId) {
            cancelAnimationFrame(this.resizeRafId);
            this.resizeRafId = null;
        }

        this.dotNetRef = null;
    }
};
