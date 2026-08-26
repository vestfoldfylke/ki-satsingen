//#region \0rolldown/runtime.js
var e = (e, t, n) => () => {
	if (n) throw n[0];
	try {
		return e && (t = e(e = 0)), t;
	} catch (e) {
		throw n = [e], e;
	}
}, t = (e, t) => () => (t || (e((t = { exports: {} }).exports, t), e = null), t.exports), n, r = e((() => {
	n = "1.20.0";
}));
//#endregion
//#region node_modules/@digdir/designsystemet-web/dist/esm/utils/utils.js
function i(e, t) {
	let n;
	return function(...r) {
		clearTimeout(n), n = setTimeout(() => e.apply(this, r), t);
	};
}
function a(e) {
	return u() ? (window.dsUseId || (window.dsUseId = 0), e && !e.id && (e.id = `:ds:${++window.dsUseId}`), e?.id || "") : `:ds:${++te}`;
}
var o, s, c, l, u, d, f, p, m, h, g, _, v, y, b, x, S, C, w, T, ee, E, te, D, ne, re, ie, ae, O = e((() => {
	r(), o = "aria-description", s = "aria-label", c = "aria-labelledby", l = {
		passive: !0,
		capture: !0
	}, u = () => typeof window < "u" && typeof document < "u", d = () => u() && /^Win/i.test(navigator.userAgentData?.platform || navigator.platform), f = typeof HTMLElement > "u" ? class {} : HTMLElement, p = (e, ...t) => !u() || window.dsWarnings === !1 || console.log(`\x1B[1mDesignsystemet:\x1B[m ${e}`, ...t), m = (e, t, n) => n === void 0 ? e.getAttribute(t) : (n === null ? e.removeAttribute(t) : e.getAttribute(t) !== n && e.setAttribute(t, n), null), h = (e, t) => getComputedStyle(e).getPropertyValue(t).trim(), g = /^["']|["']$/g, _ = (e, t) => {
		let n = m(e, t);
		return n ||= h(e, `--_ds-${t}`).replace(g, "").trim(), n || null;
	}, v = (e) => {
		let t = e?.getRootNode?.() || e?.ownerDocument;
		return t instanceof Document || t instanceof ShadowRoot ? t : e?.ownerDocument || document;
	}, y = (e) => {
		let t = e.target?.shadowRoot && e.composedPath()[0] || e.target;
		return t?.nodeType === 1 ? t : null;
	}, b = (e) => {
		let t = /* @__PURE__ */ new Set();
		for (; e;) t.add(e), e = e.nodeType === 11 ? e.host : e.assignedSlot || e.parentNode;
		return t;
	}, x = (e, ...t) => {
		let [n, ...r] = t;
		for (let t of n.split(" ")) e.addEventListener(t, ...r);
		return () => S(e, ...t);
	}, S = (e, ...t) => {
		let [n, ...r] = t;
		for (let t of n.split(" ")) e.removeEventListener(t, ...r);
	}, C = `_dsHotReloadCleanup${n}`, w = (e, t) => {
		if (u()) {
			window[C] || (window[C] = /* @__PURE__ */ new Map());
			for (let t of window[C]?.get(e) || []) t();
			window[C]?.set(e, t());
		}
	}, T = (e, t, n) => {
		let r = () => i.disconnect(), i = new MutationObserver((n) => {
			if (!u() || !e.isConnected) return r();
			t(e, n);
		});
		return t(e), i.observe(e, n), r;
	}, ee = (e, t) => {
		let n = document.createElement(e);
		if (t) for (let [e, r] of Object.entries(t)) m(n, e, r);
		return n;
	}, E = { define: (e, t) => !u() || window.customElements.get(e) || window.customElements.define(e, t) }, te = 0, ne = 0, re = 0, ie = (e) => {
		clearTimeout(re), D && (D.textContent = `${e}${ne++ % 2 ? "\xA0" : ""}`), e && (re = setTimeout(ie, 2e3, ""));
	}, ae = () => {
		document.readyState === "complete" && (D || (D = ee("div", { "aria-live": "assertive" }), D.style.overflow = "hidden", D.style.position = "fixed", D.style.whiteSpace = "nowrap", D.style.width = "1px"), D.isConnected || document.body.appendChild(D));
	}, w("announce", () => [x(document, "focus mouseover", ae, l)]);
})), oe, se, ce, le, ue, de, fe, pe, me = e((() => {
	O(), oe = ":click-delegate-hover", se = /* @__PURE__ */ new Set([
		"A",
		"BUTTON",
		"DETAILS",
		"DIALOG",
		"INPUT",
		"LABEL",
		"SELECT",
		"TEXTAREA"
	]), ce = (e) => {
		let t = e.button === 1 || e.metaKey || e.ctrlKey, n = y(e), r = e.button < 2 && fe(e);
		if (!(!r || r.contains(n))) {
			for (let e of b(n)) if (e.control === r) return;
			if (t && r instanceof HTMLAnchorElement) return window.open(r.href, void 0, r.rel);
			e.stopImmediatePropagation(), r.click();
		}
	}, de = (e) => {
		let t = y(e);
		if (!t || t === ue) return;
		ue = t;
		let n = fe(e);
		le !== n && (le && le.classList.remove(oe), n && n.classList.add(oe), le = n);
	}, fe = (e) => {
		let t;
		for (let n of e.composedPath()) {
			if (n.nodeType !== 1) continue;
			n.nodeName === "LABEL" && (n = n.control || n);
			let e = n.getAttribute("data-clickdelegatefor");
			if (e) {
				let r = v(n).getElementById(e);
				return r && !(t && t !== r) && !r.disabled && !r.readOnly ? r : void 0;
			}
			!t && pe(n) && (t = n);
		}
	}, pe = (e) => e.isContentEditable || e.popover || se.has(e.nodeName) || m(e, "role") === "button", w("clickdelegatefor", () => [x(window, "click auxclick", ce, !0), x(document, "mousemove", de, l)]);
})), he, ge, _e, ve, ye, be, xe, Se, Ce = e((() => {
	if (he = () => typeof window < "u" && window.document !== void 0 && window.navigator !== void 0, ge = he(), _e = ge ? navigator.userAgent : "", ve = /android/i.test(_e), ye = /firefox/i.test(_e), ge && /^Mac/i.test(navigator.userAgentData?.platform || navigator.platform), be = (e, t, n) => n === void 0 ? e.getAttribute(t) : (n === null ? e.removeAttribute(t) : e.getAttribute(t) !== n && e.setAttribute(t, n), null), xe = (e, t, n) => {
		let r = new MutationObserver((n) => {
			if (!he() || !e.isConnected) return i();
			t(e, n);
		}), i = Object.assign(() => r.disconnect(), { takeRecords: () => r.takeRecords() });
		return t(e), r.observe(e, n), i;
	}, Se = "__uDetailsPolyfillSummarys", he() && ve && ye && !window[Se]) {
		let e = document.getElementsByTagName("summary");
		window[Se] = xe(document, () => {
			for (let t of e) {
				let e = t.parentElement;
				be(t, "role", "button"), be(t, "aria-expanded", `${!!e?.open}`);
			}
		}, {
			attributeFilter: ["open"],
			attributes: !0,
			childList: !0,
			subtree: !0
		});
	}
})), we = e((() => {
	Ce();
})), Te, Ee, De, Oe, ke, Ae, je = e((() => {
	O(), Te = !1, Ee = (e) => {
		let { type: t, clientX: n = 0, clientY: r = 0 } = e, i = y(e);
		if (i && t === "pointerdown") for (let e of b(i)) {
			if (e.nodeName !== "DIALOG") continue;
			let t = e.getBoundingClientRect();
			Te = t.top <= r && r <= t.bottom && t.left <= n && n <= t.right;
			return;
		}
		else {
			let e = i?.nodeName === "DIALOG" && !Te && m(i, "closedby") === "any";
			Te = !1, e && setTimeout(De, 0, i);
		}
	}, De = (e) => e.open && e.close(), Oe = "--show-non-modal", ke = (e) => {
		for (let t of e.composedPath()) {
			let e = t.nodeType === 1 && m(t, "command");
			if (e === "show-modal" || e === Oe) return m(t, "aria-haspopup", "dialog");
		}
	}, Ae = ({ command: e, target: t }) => e === Oe && t instanceof HTMLDialogElement && t.show(), w("dialog", () => [
		x(document, "command", Ae, l),
		x(document, "focus", ke, l),
		x(document, "pointerdown pointerup", Ee, l)
	]);
})), Me, Ne, Pe = e((() => {
	O(), Me = u() ? document.getElementsByTagName("fieldset") : [], Ne = () => {
		for (let e of Me) {
			if (e.hasAttribute("aria-labelledby")) continue;
			let t = "";
			for (let n of e.children) {
				let e = n.nodeName;
				(e === "LEGEND" || n.getAttribute("data-field") === "description" || e === "P" && n.previousElementSibling?.nodeName === "LEGEND") && (t += `${a(n)} `);
			}
			m(e, c, t.trim() || null);
		}
	}, w("fieldset", () => [T(document, Ne, {
		childList: !0,
		subtree: !0
	})]);
})), Fe, Ie, Le, k, Re, ze, Be, Ve, He, Ue, We, Ge, Ke, qe, Je, Ye, Xe, Ze, Qe, $e, et, tt = e((() => {
	O(), Fe = "focusgroup", Ie = `${Fe}start`, Le = /* @__PURE__ */ new WeakMap(), k = /* @__PURE__ */ new Map(), Re = {
		listbox: {
			block: !0,
			wrap: !1,
			items: "option"
		},
		menu: {
			block: !0,
			wrap: !0,
			items: "menuitem"
		},
		menubar: {
			block: !1,
			wrap: !0,
			items: "menuitem"
		},
		radiogroup: {
			block: null,
			wrap: !0,
			items: "radio"
		},
		tablist: {
			block: !1,
			wrap: !0,
			items: "tab"
		},
		toolbar: {
			block: !1,
			wrap: !1,
			items: null
		}
	}, ze = !1, Be = /* @__PURE__ */ new Set([
		"AUDIO",
		"VIDEO",
		"TEXTAREA",
		"SELECT",
		"date",
		"datetime-local",
		"email",
		"month",
		"number",
		"password",
		"range",
		"search",
		"tel",
		"text",
		"time",
		"url",
		"week"
	]), Ve = u() && (Fe in HTMLElement.prototype || "focusGroup" in HTMLElement.prototype), He = (e) => ze = e, Ue = (e) => {
		if (e.defaultPrevented || e.altKey || e.metaKey || e.ctrlKey) return;
		let t = e.key === "Tab", n = e.key === "ArrowUp" || e.key === "ArrowDown", r = n || e.key === "ArrowLeft" || e.key === "ArrowRight";
		if (!t && !r && e.key !== "Home" && e.key !== "End") return;
		t && (He(!0), setTimeout(He, 100, !1));
		let i = y(e);
		if (!i || $e(i)) return;
		let a = Ge(b(i));
		if (a?.role && a?.el === i && (a = Ge(b(a.el.parentNode))), !a?.role) return;
		let o = qe(a.el, i), s = 0;
		if (t) return setTimeout(et, 0, o, et(o));
		if (e.key === "Home") s = 0;
		else if (e.key === "End") s = o.length - 1;
		else {
			let { direction: t, writingMode: r } = getComputedStyle(i), c = r.startsWith("vertical"), l = c ? r === "vertical-rl" : t === "rtl", u = e.key === "ArrowDown" || e.key === "ArrowRight", d = n !== c, f = u !== (!n && l);
			if ((a.block ?? d) !== d) return;
			s = o.indexOf(i) + (f ? 1 : -1), s = a.wrap ? (o.length + s) % o.length : Math.max(0, Math.min(s, o.length - 1));
		}
		o[s] !== i && e.preventDefault(), o[s]?.focus?.();
	}, We = (e) => {
		let t = y(e), n = b(t);
		for (let [e, t] of k) !n.has(e) && k.delete(e) && t();
		for (let e of n) e.nodeType === 11 && !k.has(e) && k.set(e, x(e, "focus", We, l));
		if (e.target?.shadowRoot) return;
		let r = Ge(n);
		if (r?.role) {
			if (ze && !$e(r.focus)) {
				let e = Je(qe(r.el, null), t), n = r.memory && e.includes(r.focus) && r.focus || e.find((e) => e?.hasAttribute(Ie)) || e[0];
				if (n !== t) return n?.focus?.();
			}
			r.focus = t;
		}
	}, Ge = (e) => {
		for (let t of e) {
			if (t.nodeType !== Node.ELEMENT_NODE) continue;
			let e = t.getAttribute(Fe);
			if (e !== null) {
				let n = Le.get(t);
				if (n?.key === e) return n;
				n = Ke(t, e), Le.set(t, n);
				for (let e of qe(t)) e && !e.hasAttribute("role") && m(e, "role", n.items);
				return t.hasAttribute("role") || m(t, "role", n.role), n;
			}
			if (Xe(t)) return;
		}
	}, Ke = (e, t) => {
		let n = new Set(t.toLowerCase().split(" ")), r = [...n].find((e) => e in Re), i = Re[r], a = n.has("inline"), o = n.has("block");
		return {
			key: t,
			el: e,
			role: r,
			block: a && o ? null : o || !a && i?.block,
			focus: null,
			items: i?.items,
			memory: !n.has("nomemory"),
			wrap: n.has("wrap") || !n.has("nowrap") && i?.wrap
		};
	}, qe = (e, t, n = [], r = !1) => {
		let i = e?.nodeName === "SLOT" ? e.assignedElements({ flatten: !0 }) : (e?.shadowRoot || e)?.children;
		for (let e = 0, a = i?.length || e; i && e < a; e++) {
			let a = i[e];
			if (a.nodeName === "SLOT") qe(a, t, n, r);
			else if (Ye(a) && !Xe(a) && Ze(a)) {
				let e = a.getAttribute(Fe);
				a === t || !r && e !== "none" && Qe(a) ? n.push(a) : e !== null && t === null ? n.push(null) : qe(a, t, n, r || e !== null);
			}
		}
		return n;
	}, Je = (e, t) => {
		let n = e.indexOf(t), r = e.indexOf(null, n);
		return e.slice(e.lastIndexOf(null, n) + 1, r === -1 ? void 0 : r);
	}, Ye = (e) => !e.inert && !e.hidden && !e.disabled, Xe = (e) => e?.nodeName === "DIALOG" || e?.hasAttribute("popover"), Ze = u() && typeof Element.prototype.checkVisibility == "function" ? (e) => e.checkVisibility() : (e) => e.offsetParent !== null, Qe = (e) => e.isContentEditable || e.tabIndex >= 0 && !e.disabled, $e = (e) => e?.isContentEditable || Be.has(e?.nodeName) || e?.nodeName === "INPUT" && Be.has(e.type), et = (e, t) => Array.from(e, (e, n) => {
		let r = e?.getAttribute("tabindex");
		return e && m(e, "tabindex", t ? t[n] : "-1"), r;
	}), Ve || w(Fe, () => [
		x(document, "keydown", Ue),
		x(document, "focus", We, l),
		() => {
			for (let [, e] of k) e();
			k.clear();
		}
	]);
}));
//#endregion
//#region node_modules/@digdir/designsystemet-web/dist/esm/_vendors/invokers-polyfill/invoker.js
function nt() {
	return typeof HTMLButtonElement < "u" && "command" in HTMLButtonElement.prototype && "source" in ((globalThis.CommandEvent || {}).prototype || {});
}
function rt() {
	document.addEventListener("invoke", (e) => {
		e.type == "invoke" && e.isTrusted && (e.stopImmediatePropagation(), e.preventDefault());
	}, !0), document.addEventListener("command", (e) => {
		e.type == "command" && e.isTrusted && (e.stopImmediatePropagation(), e.preventDefault());
	}, !0);
	function e(e, t, n = !0) {
		Object.defineProperty(e, t, {
			...Object.getOwnPropertyDescriptor(e, t),
			enumerable: n
		});
	}
	function t(e) {
		return e && typeof e.getRootNode == "function" ? e.getRootNode() : e && e.parentNode ? t(e.parentNode) : e;
	}
	let n = /* @__PURE__ */ new WeakMap(), r = /* @__PURE__ */ new WeakMap();
	class i extends Event {
		constructor(e, t = {}) {
			super(e, t);
			let { source: i, command: a } = t;
			if (i != null && !(i instanceof Element)) throw TypeError("source must be an element");
			n.set(this, i || null), r.set(this, a === void 0 ? "" : String(a));
		}
		get [Symbol.toStringTag]() {
			return "CommandEvent";
		}
		get source() {
			if (!n.has(this)) throw TypeError("illegal invocation");
			let e = n.get(this);
			if (!(e instanceof Element)) return null;
			let r = t(e);
			return r === t(this.target || document) ? e : r.host;
		}
		get command() {
			if (!r.has(this)) throw TypeError("illegal invocation");
			return r.get(this);
		}
	}
	e(i.prototype, "source"), e(i.prototype, "command");
	let a = /* @__PURE__ */ new WeakMap();
	function o(e) {
		Object.defineProperties(e.prototype, {
			commandForElement: {
				enumerable: !0,
				configurable: !0,
				set(e) {
					if (e === null) this.removeAttribute("commandfor"), a.delete(this);
					else if (e instanceof Element) {
						this.setAttribute("commandfor", "");
						let n = t(e);
						t(this) === n || n === this.ownerDocument ? a.set(this, e) : a.delete(this);
					} else throw TypeError("commandForElement must be an element or null");
				},
				get() {
					if (this.localName !== "button" || this.disabled) return null;
					if (this.form && this.getAttribute("type") !== "button") return console.warn("Element with `commandFor` is a form participant. It should explicitly set `type=button` in order for `commandFor` to work"), null;
					let e = a.get(this);
					if (e) return e.isConnected ? e : (a.delete(this), null);
					let n = t(this), r = this.getAttribute("commandfor");
					return (n instanceof Document || n instanceof ShadowRoot) && r && n.getElementById(r) || null;
				}
			},
			command: {
				enumerable: !0,
				configurable: !0,
				get() {
					let e = this.getAttribute("command") || "";
					if (e.startsWith("--")) return e;
					let t = e.toLowerCase();
					switch (t) {
						case "show-modal":
						case "request-close":
						case "close":
						case "toggle-popover":
						case "hide-popover":
						case "show-popover": return t;
					}
					return "";
				},
				set(e) {
					this.setAttribute("command", e);
				}
			}
		});
	}
	let s = /* @__PURE__ */ new WeakMap();
	Object.defineProperties(HTMLElement.prototype, { oncommand: {
		enumerable: !0,
		configurable: !0,
		get() {
			return l.takeRecords(), s.get(this) || null;
		},
		set(e) {
			let t = s.get(this) || null;
			t && this.removeEventListener("command", t), s.set(this, typeof e == "object" || typeof e == "function" ? e : null), typeof e == "function" && this.addEventListener("command", e);
		}
	} });
	function c(e) {
		for (let t of e) t.oncommand = Function("event", t.getAttribute("oncommand"));
	}
	let l = new MutationObserver((e) => {
		for (let t of e) {
			let { target: e } = t;
			t.type === "childList" ? c(e.querySelectorAll("[oncommand]")) : c([e]);
		}
	});
	l.observe(document, {
		subtree: !0,
		childList: !0,
		attributeFilter: ["oncommand"]
	}), c(document.querySelectorAll("[oncommand]"));
	let u = /* @__PURE__ */ new WeakSet();
	function d(e) {
		if (u.has(e) || (u.add(e), e.defaultPrevented) || e.type !== "click") return;
		let t = e.composedPath().find((e) => e.matches?.("button[commandfor], button[command]"));
		if (!t) return;
		if (t.form && t.getAttribute("type") !== "button") throw e.preventDefault(), Error("Element with `commandFor` is a form participant. It should explicitly set `type=button` in order for `commandFor` to work. In order for it to act as a Submit button, it must not have command or commandfor attributes");
		if (t.hasAttribute("command") !== t.hasAttribute("commandfor")) {
			let e = t.hasAttribute("command") ? "command" : "commandfor", n = t.hasAttribute("command") ? "commandfor" : "command";
			throw Error(`Element with ${e} attribute must also have a ${n} attribute to function.`);
		}
		if (t.command !== "show-popover" && t.command !== "hide-popover" && t.command !== "toggle-popover" && t.command !== "show-modal" && t.command !== "request-close" && t.command !== "close" && !t.command.startsWith("--")) {
			console.warn(`"${t.command}" is not a valid command value. Custom commands must begin with --`);
			return;
		}
		let n = t.commandForElement;
		if (!n) return;
		let r = new i("command", {
			command: t.command,
			source: t,
			cancelable: !0
		});
		if (n.dispatchEvent(r), r.defaultPrevented) return;
		let a = r.command.toLowerCase();
		if (n.popover) {
			let e = !n.matches(":popover-open");
			e && (a === "toggle-popover" || a === "show-popover") ? n.showPopover({ source: t }) : !e && a === "hide-popover" && n.hidePopover();
		} else if (n.localName === "dialog") {
			let e = !n.hasAttribute("open");
			e && a == "show-modal" ? n.showModal() : !e && a == "close" ? n.close(t.value ? t.value : void 0) : !e && a == "request-close" && (HTMLDialogElement.prototype.requestClose || (HTMLDialogElement.prototype.requestClose = function() {
				let e = new Event("cancel", { cancelable: !0 });
				this.dispatchEvent(e), e.defaultPrevented || this.close();
			}), n.requestClose(t.value ? t.value : void 0));
		}
	}
	function f(e) {
		e.addEventListener("click", d, !0);
	}
	function p(e, t) {
		let n = e.prototype.attachShadow;
		e.prototype.attachShadow = function(e) {
			let r = n.call(this, e);
			return t(r), r;
		};
		let r = e.prototype.attachInternals;
		e.prototype.attachInternals = function() {
			let e = r.call(this);
			return e.shadowRoot && t(e.shadowRoot), e;
		};
	}
	o(HTMLButtonElement), p(HTMLElement, (e) => {
		f(e), l.observe(e, { attributeFilter: ["oncommand"] }), c(e.querySelectorAll("[oncommand]"));
	}), f(document), Object.assign(globalThis, { CommandEvent: i });
}
var it = e((() => {})), at = e((() => {
	O(), it(), u() && !nt() && rt();
}));
//#endregion
//#region node_modules/@digdir/designsystemet-web/dist/esm/_vendors/@oddbird/popover-polyfill/dist/popover-fn.js
function ot(e, t, n) {
	Rt.set(e, setTimeout(() => {
		Rt.has(e) && e.dispatchEvent(new Lt("toggle", {
			cancelable: !1,
			oldState: t,
			newState: n
		}));
	}, 0));
}
function st(e) {
	return Ht.get(e) || "hidden";
}
function ct(e) {
	return [...e].pop();
}
function lt(e) {
	let t = e.popoverTargetElement;
	if (!(t instanceof HTMLElement)) return;
	let n = st(t);
	(e.popoverTargetAction !== "show" || n !== "showing") && (e.popoverTargetAction !== "hide" || n !== "hidden") && (n === "showing" ? Ct(t, !0, !0) : ut(t, !1) && (Ut.set(t, e), St(t)));
}
function ut(e, t) {
	return !(e.popover !== "auto" && e.popover !== "manual" && e.popover !== "hint" || !e.isConnected || t && st(e) !== "showing" || !t && st(e) !== "hidden" || e instanceof Bt && e.hasAttribute("open") || document.fullscreenElement === e);
}
function dt(e) {
	if (!e) return 0;
	let t = A.get(document) || /* @__PURE__ */ new Set(), n = j.get(document) || /* @__PURE__ */ new Set();
	return n.has(e) ? [...n].indexOf(e) + t.size + 1 : t.has(e) ? [...t].indexOf(e) + 1 : 0;
}
function ft(e) {
	let t = gt(e), n = _t(e);
	return dt(t) > dt(n) ? t : n;
}
function pt(e) {
	let t, n = j.get(e) || /* @__PURE__ */ new Set(), r = A.get(e) || /* @__PURE__ */ new Set(), i = n.size > 0 ? n : r.size > 0 ? r : null;
	return i ? (t = ct(i), t.isConnected ? t : (i.delete(t), pt(e))) : null;
}
function mt(e) {
	for (let t of e || []) if (!t.isConnected) e.delete(t);
	else return t;
	return null;
}
function ht(e) {
	return typeof e.getRootNode == "function" ? e.getRootNode() : e.parentNode ? ht(e.parentNode) : e;
}
function gt(e) {
	for (; e;) {
		if (e instanceof HTMLElement && e.popover === "auto" && Ht.get(e) === "showing") return e;
		if (e = e instanceof Element && e.assignedSlot || e.parentElement || ht(e), e instanceof zt && (e = e.host), e instanceof Document) return;
	}
}
function _t(e) {
	for (; e;) {
		let t = e.popoverTargetElement;
		if (t instanceof HTMLElement) return t;
		if (e = e.parentElement || ht(e), e instanceof zt && (e = e.host), e instanceof Document) return;
	}
}
function vt(e, t) {
	let n = /* @__PURE__ */ new Map(), r = 0;
	for (let e of t || []) n.set(e, r), r += 1;
	n.set(e, r), r += 1;
	let i = null;
	function a(t) {
		if (!t) return;
		let r = !1, a = null;
		for (; !r;) {
			if (a = gt(t) || null, a === null || !n.has(a)) return;
			(e.popover === "hint" || a.popover === "auto") && (r = !0), r || (t = a.parentElement);
		}
		let o = n.get(a) ?? null;
		(i === null || n.get(i) < o) && (i = a);
	}
	return a(e.parentElement || ht(e)), i;
}
function yt(e) {
	return e.hidden || e instanceof zt || (e instanceof HTMLButtonElement || e instanceof HTMLInputElement || e instanceof HTMLSelectElement || e instanceof HTMLTextAreaElement || e instanceof HTMLOptGroupElement || e instanceof HTMLOptionElement || e instanceof HTMLFieldSetElement) && e.disabled || e instanceof HTMLInputElement && e.type === "hidden" || e instanceof HTMLAnchorElement && e.href === "" ? !1 : typeof e.tabIndex == "number" && e.tabIndex !== -1;
}
function bt(e) {
	if (e.shadowRoot && e.shadowRoot.delegatesFocus !== !0) return null;
	let t = e;
	t.shadowRoot && (t = t.shadowRoot);
	let n = t.querySelector("[autofocus]");
	if (n) return n;
	{
		let e = t.querySelectorAll("slot");
		for (let t of e) {
			let e = t.assignedElements({ flatten: !0 });
			for (let t of e) if (t.hasAttribute("autofocus")) return t;
			else if (n = t.querySelector("[autofocus]"), n) return n;
		}
	}
	let r = e.ownerDocument.createTreeWalker(t, NodeFilter.SHOW_ELEMENT), i = r.currentNode;
	for (; i;) {
		if (yt(i)) return i;
		i = r.nextNode();
	}
}
function xt(e) {
	var t;
	(t = bt(e)) == null || t.focus();
}
function St(e) {
	if (!ut(e, !1)) return;
	let t = e.ownerDocument;
	if (!e.dispatchEvent(new Lt("beforetoggle", {
		cancelable: !0,
		oldState: "closed",
		newState: "open"
	})) || !ut(e, !1)) return;
	let n = !1, r = e.popover, i = null, a = vt(e, A.get(t) || /* @__PURE__ */ new Set()), o = vt(e, j.get(t) || /* @__PURE__ */ new Set());
	if (r === "auto" && (Tt(j.get(t) || /* @__PURE__ */ new Set(), n, !0), Dt(a || t, n, !0), i = "auto"), r === "hint" && (o ? (Dt(o, n, !0), i = "hint") : (Tt(j.get(t) || /* @__PURE__ */ new Set(), n, !0), a ? (Dt(a, n, !0), i = "auto") : i = "hint")), r === "auto" || r === "hint") {
		if (r !== e.popover || !ut(e, !1)) return;
		pt(t) || (n = !0), i === "auto" ? (A.has(t) || A.set(t, /* @__PURE__ */ new Set()), A.get(t).add(e)) : i === "hint" && (j.has(t) || j.set(t, /* @__PURE__ */ new Set()), j.get(t).add(e));
	}
	Wt.delete(e);
	let s = t.activeElement;
	e.classList.add(":popover-open"), Ht.set(e, "showing"), Vt.has(t) || Vt.set(t, /* @__PURE__ */ new Set()), Vt.get(t).add(e), kt(Ut.get(e), !0), xt(e), n && s && e.popover === "auto" && Wt.set(e, s), ot(e, "closed", "open");
}
function Ct(e, t = !1, n = !1) {
	var r, i;
	if (!ut(e, !0)) return;
	let a = e.ownerDocument;
	if (["auto", "hint"].includes(e.popover) && (Dt(e, t, n), !ut(e, !0))) return;
	let o = A.get(a) || /* @__PURE__ */ new Set(), s = o.has(e) && ct(o) === e;
	if (kt(Ut.get(e), !1), Ut.delete(e), n && (e.dispatchEvent(new Lt("beforetoggle", {
		oldState: "open",
		newState: "closed"
	})), s && ct(o) !== e && Dt(e, t, n), !ut(e, !0))) return;
	(r = Vt.get(a)) == null || r.delete(e), o.delete(e), (i = j.get(a)) == null || i.delete(e), e.classList.remove(":popover-open"), Ht.set(e, "hidden"), n && ot(e, "open", "closed");
	let c = Wt.get(e);
	c && (Wt.delete(e), t && c.focus());
}
function wt(e, t = !1, n = !1) {
	let r = pt(e);
	for (; r;) Ct(r, t, n), r = pt(e);
}
function Tt(e, t = !1, n = !1) {
	let r = mt(e);
	for (; r;) Ct(r, t, n), r = mt(e);
}
function Et(e, t, n, r) {
	let i = !1, a = !1;
	for (; i || !a;) {
		a = !0;
		let o = null, s = !1;
		for (let n of t) if (n === e) s = !0;
		else if (s) {
			o = n;
			break;
		}
		if (!o) return;
		for (; st(o) === "showing" && t.size;) Ct(ct(t), n, r);
		t.has(e) && ct(t) !== e && (i = !0), i && (r = !1);
	}
}
function Dt(e, t, n) {
	let r = e.ownerDocument || e;
	if (e instanceof Document) return wt(r, t, n);
	if (j.get(r)?.has(e)) {
		Et(e, j.get(r), t, n);
		return;
	}
	Tt(j.get(r) || /* @__PURE__ */ new Set(), t, n), A.get(r)?.has(e) && Et(e, A.get(r), t, n);
}
function Ot(e) {
	if (!e.isTrusted) return;
	let t = e.composedPath()[0];
	if (!t) return;
	let n = t.ownerDocument;
	if (!pt(n)) return;
	let r = ft(t);
	if (r && e.type === "pointerdown") Gt.set(n, r);
	else if (e.type === "pointerup") {
		let e = Gt.get(n) === r;
		Gt.delete(n), e && Dt(r || n, !1, !0);
	}
}
function kt(e, t = !1) {
	if (!e) return;
	Kt.has(e) || Kt.set(e, e.getAttribute("aria-expanded"));
	let n = e.popoverTargetElement;
	if (n instanceof HTMLElement && n.popover === "auto") e.setAttribute("aria-expanded", String(t));
	else {
		let t = Kt.get(e);
		t ? e.setAttribute("aria-expanded", t) : e.removeAttribute("aria-expanded");
	}
}
function At() {
	return typeof HTMLElement < "u" && typeof HTMLElement.prototype == "object" && "popover" in HTMLElement.prototype && "showPopover" in HTMLElement.prototype;
}
function jt() {
	return !!(document.body?.showPopover && !/native code/i.test(document.body.showPopover.toString()));
}
function Mt(e, t, n) {
	let r = e[t];
	Object.defineProperty(e, t, { value(e) {
		return r.call(this, n(e));
	} });
}
function Nt() {
	return typeof globalThis.CSSLayerBlockRule == "function";
}
function Pt(e) {
	let t = Nt(), n = (e ?? window.POPOVER_POLYFILL_OPTIONS?.layerName ?? Jt).split(".").map((e) => CSS.escape(e)).join(".");
	return `
${t ? `@layer ${n} {` : ""}
  :where([popover]) {
    position: fixed;
    z-index: 2147483647;
    inset: 0;
    padding: 0.25em;
    width: fit-content;
    height: fit-content;
    border-width: initial;
    border-color: initial;
    border-image: initial;
    border-style: solid;
    background-color: canvas;
    color: canvastext;
    overflow: auto;
    margin: auto;
  }

  :where([popover]:not(.\\:popover-open)) {
    display: none;
  }

  :where(dialog[popover].\\:popover-open) {
    display: block;
  }

  :where(dialog[popover][open]) {
    display: revert;
  }

  :where([anchor].\\:popover-open) {
    inset: auto;
  }

  :where([anchor]:popover-open) {
    inset: auto;
  }

  @supports not (background-color: canvas) {
    :where([popover]) {
      background-color: white;
      color: black;
    }
  }

  @supports (width: -moz-fit-content) {
    :where([popover]) {
      width: -moz-fit-content;
      height: -moz-fit-content;
    }
  }

  @supports not (inset: 0) {
    :where([popover]) {
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
    }
  }
${t ? "}" : ""}
`;
}
function Ft(e, t) {
	let n = Pt(t);
	if (Xt === null) try {
		Xt = new CSSStyleSheet(), Xt.replaceSync(n);
	} catch {
		Xt = !1;
	}
	if (Xt === !1 || !e.adoptedStyleSheets) {
		let t = document.createElement("style");
		t.textContent = n, e instanceof Document ? e.head.prepend(t) : e.prepend(t);
	} else e.adoptedStyleSheets = [Xt, ...e.adoptedStyleSheets];
}
function It(e) {
	if (typeof window > "u") return;
	let t = e?.layerName;
	window.ToggleEvent = window.ToggleEvent || Lt;
	function n(e) {
		return e?.includes(":popover-open") && (e = e.replace(Yt, "$1.\\:popover-open")), e;
	}
	Mt(Document.prototype, "querySelector", n), Mt(Document.prototype, "querySelectorAll", n), Mt(Element.prototype, "querySelector", n), Mt(Element.prototype, "querySelectorAll", n), Mt(Element.prototype, "matches", n), Mt(Element.prototype, "closest", n), Mt(DocumentFragment.prototype, "querySelectorAll", n), Object.defineProperties(HTMLElement.prototype, {
		popover: {
			enumerable: !0,
			configurable: !0,
			get() {
				if (!this.hasAttribute("popover")) return null;
				let e = (this.getAttribute("popover") || "").toLowerCase();
				return e === "" || e == "auto" ? "auto" : e == "hint" ? "hint" : "manual";
			},
			set(e) {
				e === null ? this.removeAttribute("popover") : this.setAttribute("popover", e);
			}
		},
		showPopover: {
			enumerable: !0,
			configurable: !0,
			writable: !0,
			value(e = {}) {
				St(this);
			}
		},
		hidePopover: {
			enumerable: !0,
			configurable: !0,
			writable: !0,
			value() {
				Ct(this, !0, !0);
			}
		},
		togglePopover: {
			enumerable: !0,
			configurable: !0,
			writable: !0,
			value(e = {}) {
				return typeof e == "boolean" && (e = { force: e }), Ht.get(this) === "showing" && e.force === void 0 || e.force === !1 ? Ct(this, !0, !0) : (e.force === void 0 || e.force === !0) && St(this), Ht.get(this) === "showing";
			}
		}
	});
	let r = Element.prototype.attachShadow;
	r && Object.defineProperties(Element.prototype, { attachShadow: {
		enumerable: !0,
		configurable: !0,
		writable: !0,
		value(e) {
			let n = r.call(this, e);
			return Ft(n, t), n;
		}
	} });
	let i = HTMLElement.prototype.attachInternals;
	i && Object.defineProperties(HTMLElement.prototype, { attachInternals: {
		enumerable: !0,
		configurable: !0,
		writable: !0,
		value() {
			let e = i.call(this);
			return e.shadowRoot && Ft(e.shadowRoot, t), e;
		}
	} });
	let a = /* @__PURE__ */ new WeakMap();
	function o(e) {
		Object.defineProperties(e.prototype, {
			popoverTargetElement: {
				enumerable: !0,
				configurable: !0,
				set(e) {
					if (e === null) this.removeAttribute("popovertarget"), a.delete(this);
					else if (e instanceof Element) this.setAttribute("popovertarget", ""), a.set(this, e);
					else throw TypeError("popoverTargetElement must be an element or null");
				},
				get() {
					if (this.localName !== "button" && this.localName !== "input" || this.localName === "input" && this.type !== "reset" && this.type !== "image" && this.type !== "button" || this.disabled || this.form && this.type === "submit") return null;
					let e = a.get(this);
					if (e && e.isConnected) return e;
					if (e && !e.isConnected) return a.delete(this), null;
					let t = ht(this), n = this.getAttribute("popovertarget");
					return (t instanceof Document || t instanceof qt) && n && t.getElementById(n) || null;
				}
			},
			popoverTargetAction: {
				enumerable: !0,
				configurable: !0,
				get() {
					let e = (this.getAttribute("popovertargetaction") || "").toLowerCase();
					return e === "show" || e === "hide" ? e : "toggle";
				},
				set(e) {
					this.setAttribute("popovertargetaction", e);
				}
			}
		});
	}
	o(HTMLButtonElement), o(HTMLInputElement);
	let s = (e) => {
		if (e.defaultPrevented) return;
		let t = e.composedPath(), n = t[0];
		if (!(n instanceof Element) || n?.shadowRoot) return;
		let r = ht(n);
		if (!(r instanceof qt || r instanceof Document)) return;
		let i = t.find((e) => e.matches?.call(e, "[popovertargetaction],[popovertarget]"));
		if (i) {
			lt(i), e.preventDefault();
			return;
		}
	}, c = (e) => {
		let t = e.key, n = e.target;
		!e.defaultPrevented && n && (t === "Escape" || t === "Esc") && Dt(n.ownerDocument || n, !0, !0);
	};
	((e) => {
		e.addEventListener("click", s), e.addEventListener("keydown", c), e.addEventListener("pointerdown", Ot), e.addEventListener("pointerup", Ot);
	})(document), Ft(document, t);
}
var Lt, Rt, zt, Bt, Vt, A, j, Ht, Ut, Wt, Gt, Kt, qt, Jt, Yt, Xt, Zt = e((() => {
	Lt = class extends Event {
		oldState;
		newState;
		constructor(e, t = {}) {
			let n = Object.assign({}, t);
			delete n.oldState, delete n.newState, super(e, n), this.oldState = String(t.oldState || ""), this.newState = String(t.newState || "");
		}
	}, Rt = /* @__PURE__ */ new WeakMap(), zt = globalThis.ShadowRoot || function() {}, Bt = globalThis.HTMLDialogElement || function() {}, Vt = /* @__PURE__ */ new WeakMap(), A = /* @__PURE__ */ new WeakMap(), j = /* @__PURE__ */ new WeakMap(), Ht = /* @__PURE__ */ new WeakMap(), Ut = /* @__PURE__ */ new WeakMap(), Wt = /* @__PURE__ */ new WeakMap(), Gt = /* @__PURE__ */ new WeakMap(), Kt = /* @__PURE__ */ new WeakMap(), qt = globalThis.ShadowRoot || function() {}, Jt = "popover-polyfill", Yt = /(^|[^\\]):popover-open\b/g, Xt = null;
}));
//#endregion
//#region node_modules/@floating-ui/utils/dist/floating-ui.utils.mjs
function Qt(e, t, n) {
	return P(e, mn(t, n));
}
function $t(e, t) {
	return typeof e == "function" ? e(t) : e;
}
function M(e) {
	return e.split("-")[0];
}
function en(e) {
	return e.split("-")[1];
}
function tn(e) {
	return e === "x" ? "y" : "x";
}
function nn(e) {
	return e === "y" ? "height" : "width";
}
function N(e) {
	let t = e[0];
	return t === "t" || t === "b" ? "y" : "x";
}
function rn(e) {
	return tn(N(e));
}
function an(e, t, n) {
	n === void 0 && (n = !1);
	let r = en(e), i = rn(e), a = nn(i), o = i === "x" ? r === (n ? "end" : "start") ? "right" : "left" : r === "start" ? "bottom" : "top";
	return t.reference[a] > t.floating[a] && (o = un(o)), [o, un(o)];
}
function on(e) {
	let t = un(e);
	return [
		sn(e),
		t,
		sn(t)
	];
}
function sn(e) {
	return e.includes("start") ? e.replace("start", "end") : e.replace("end", "start");
}
function cn(e, t, n) {
	switch (e) {
		case "top":
		case "bottom": return n ? t ? yn : vn : t ? vn : yn;
		case "left":
		case "right": return t ? bn : xn;
		default: return [];
	}
}
function ln(e, t, n, r) {
	let i = en(e), a = cn(M(e), n === "start", r);
	return i && (a = a.map((e) => e + "-" + i), t && (a = a.concat(a.map(sn)))), a;
}
function un(e) {
	let t = M(e);
	return _n[t] + e.slice(t.length);
}
function dn(e) {
	return {
		top: e.top ?? 0,
		right: e.right ?? 0,
		bottom: e.bottom ?? 0,
		left: e.left ?? 0
	};
}
function fn(e) {
	return typeof e == "number" ? {
		top: e,
		right: e,
		bottom: e,
		left: e
	} : dn(e);
}
function pn(e) {
	let { x: t, y: n, width: r, height: i } = e;
	return {
		width: r,
		height: i,
		top: n,
		left: t,
		right: t + r,
		bottom: n + i,
		x: t,
		y: n
	};
}
var mn, P, hn, gn, F, _n, vn, yn, bn, xn, Sn = e((() => {
	mn = Math.min, P = Math.max, hn = Math.round, gn = Math.floor, F = (e) => ({
		x: e,
		y: e
	}), _n = {
		left: "right",
		right: "left",
		bottom: "top",
		top: "bottom"
	}, vn = ["left", "right"], yn = ["right", "left"], bn = ["top", "bottom"], xn = ["bottom", "top"];
}));
//#endregion
//#region node_modules/@floating-ui/core/dist/floating-ui.core.mjs
function Cn(e, t, n) {
	let { reference: r, floating: i } = e, a = N(t), o = rn(t), s = nn(o), c = M(t), l = a === "y", u = r.x + r.width / 2 - i.width / 2, d = r.y + r.height / 2 - i.height / 2, f = r[s] / 2 - i[s] / 2, p;
	switch (c) {
		case "top":
			p = {
				x: u,
				y: r.y - i.height
			};
			break;
		case "bottom":
			p = {
				x: u,
				y: r.y + r.height
			};
			break;
		case "right":
			p = {
				x: r.x + r.width,
				y: d
			};
			break;
		case "left":
			p = {
				x: r.x - i.width,
				y: d
			};
			break;
		default: p = {
			x: r.x,
			y: r.y
		};
	}
	let m = en(t);
	return m && (p[o] += f * (m === "end" ? 1 : -1) * (n && l ? -1 : 1)), p;
}
async function wn(e, t) {
	t === void 0 && (t = {});
	let { x: n, y: r, platform: i, rects: a, elements: o, strategy: s } = e, { boundary: c = "clippingAncestors", rootBoundary: l = "viewport", elementContext: u = "floating", altBoundary: d = !1, padding: f = 0 } = $t(t, e), p = fn(f), m = o[d ? u === "floating" ? "reference" : "floating" : u], h = pn(await i.getClippingRect({
		element: await (i.isElement == null ? void 0 : i.isElement(m)) ?? !0 ? m : m.contextElement || await (i.getDocumentElement == null ? void 0 : i.getDocumentElement(o.floating)),
		boundary: c,
		rootBoundary: l,
		strategy: s
	})), g = u === "floating" ? {
		x: n,
		y: r,
		width: a.floating.width,
		height: a.floating.height
	} : a.reference, _ = await (i.getOffsetParent == null ? void 0 : i.getOffsetParent(o.floating)), v = await (i.isElement == null ? void 0 : i.isElement(_)) && await (i.getScale == null ? void 0 : i.getScale(_)) || {
		x: 1,
		y: 1
	}, y = pn(i.convertOffsetParentRelativeRectToViewportRelativeRect ? await i.convertOffsetParentRelativeRectToViewportRelativeRect({
		elements: o,
		rect: g,
		offsetParent: _,
		strategy: s
	}) : g);
	return {
		top: (h.top - y.top + p.top) / v.y,
		bottom: (y.bottom - h.bottom + p.bottom) / v.y,
		left: (h.left - y.left + p.left) / v.x,
		right: (y.right - h.right + p.right) / v.x
	};
}
async function Tn(e, t) {
	let { placement: n, platform: r, elements: i } = e, a = await (r.isRTL == null ? void 0 : r.isRTL(i.floating)), o = M(n), s = en(n), c = N(n) === "y", l = kn.has(o) ? -1 : 1, u = a && c ? -1 : 1, d = $t(t, e), { mainAxis: f, crossAxis: p, alignmentAxis: m } = typeof d == "number" ? {
		mainAxis: d,
		crossAxis: 0,
		alignmentAxis: null
	} : {
		mainAxis: d.mainAxis || 0,
		crossAxis: d.crossAxis || 0,
		alignmentAxis: d.alignmentAxis
	};
	return s && typeof m == "number" && (p = s === "end" ? m * -1 : m), c ? {
		x: p * u,
		y: f * l
	} : {
		x: f * l,
		y: p * u
	};
}
var En, Dn, On, kn, An, jn, Mn, Nn, Pn = e((() => {
	Sn(), En = 50, Dn = async (e, t, n) => {
		let { placement: r = "bottom", strategy: i = "absolute", middleware: a = [], platform: o } = n, s = o.detectOverflow ? o : {
			...o,
			detectOverflow: wn
		}, c = await (o.isRTL == null ? void 0 : o.isRTL(t)), l = await o.getElementRects({
			reference: e,
			floating: t,
			strategy: i
		}), { x: u, y: d } = Cn(l, r, c), f = r, p = 0, m = {};
		for (let n = 0; n < a.length; n++) {
			let h = a[n];
			if (!h) continue;
			let { name: g, fn: _ } = h, { x: v, y, data: b, reset: x } = await _({
				x: u,
				y: d,
				initialPlacement: r,
				placement: f,
				strategy: i,
				middlewareData: m,
				rects: l,
				platform: s,
				elements: {
					reference: e,
					floating: t
				}
			});
			u = v ?? u, d = y ?? d, m[g] = {
				...m[g],
				...b
			}, x && p < En && (p++, typeof x == "object" && (x.placement && (f = x.placement), x.rects && (l = x.rects === !0 ? await o.getElementRects({
				reference: e,
				floating: t,
				strategy: i
			}) : x.rects), {x: u, y: d} = Cn(l, f, c)), n = -1);
		}
		return {
			x: u,
			y: d,
			placement: f,
			strategy: i,
			middlewareData: m
		};
	}, On = function(e) {
		return e === void 0 && (e = {}), {
			name: "flip",
			options: e,
			async fn(t) {
				var n;
				let { placement: r, middlewareData: i, rects: a, initialPlacement: o, platform: s, elements: c } = t, { mainAxis: l = !0, crossAxis: u = !0, fallbackPlacements: d, fallbackStrategy: f = "bestFit", fallbackAxisSideDirection: p = "none", flipAlignment: m = !0, ...h } = $t(e, t);
				if ((n = i.arrow) != null && n.alignmentOffset) return {};
				let g = M(r), _ = N(o), v = M(o) === o, y = await (s.isRTL == null ? void 0 : s.isRTL(c.floating)), b = d || (v || !m ? [un(o)] : on(o)), x = p !== "none";
				!d && x && b.push(...ln(o, m, p, y));
				let S = [o, ...b], C = await s.detectOverflow(t, h), w = [], T = i.flip?.overflows || [];
				if (l && w.push(C[g]), u) {
					let e = an(r, a, y);
					w.push(C[e[0]], C[e[1]]);
				}
				if (T = [...T, {
					placement: r,
					overflows: w
				}], !w.every((e) => e <= 0)) {
					let e = (i.flip?.index || 0) + 1, t = S[e];
					if (t && (u !== "alignment" || _ === N(t) || T.every((e) => N(e.placement) !== _ || e.overflows[0] > 0))) return {
						data: {
							index: e,
							overflows: T
						},
						reset: { placement: t }
					};
					let n = T.filter((e) => e.overflows[0] <= 0).sort((e, t) => e.overflows[1] - t.overflows[1])[0]?.placement;
					if (!n) switch (f) {
						case "bestFit": {
							let e = T.filter((e) => {
								if (x) {
									let t = N(e.placement);
									return t === _ || t === "y";
								}
								return !0;
							}).map((e) => [e.placement, e.overflows.filter((e) => e > 0).reduce((e, t) => e + t, 0)]).sort((e, t) => e[1] - t[1])[0]?.[0];
							e && (n = e);
							break;
						}
						case "initialPlacement": n = o;
					}
					if (r !== n) return { reset: { placement: n } };
				}
				return {};
			}
		};
	}, kn = /*#__PURE__*/ new Set(["left", "top"]), An = function(e) {
		return e === void 0 && (e = 0), {
			name: "offset",
			options: e,
			async fn(t) {
				var n;
				let { x: r, y: i, placement: a, middlewareData: o } = t, s = await Tn(t, e);
				return a === o.offset?.placement && (n = o.arrow) != null && n.alignmentOffset ? {} : {
					x: r + s.x,
					y: i + s.y,
					data: {
						...s,
						placement: a
					}
				};
			}
		};
	}, jn = function(e) {
		return e === void 0 && (e = {}), {
			name: "shift",
			options: e,
			async fn(t) {
				let { x: n, y: r, placement: i, platform: a } = t, { mainAxis: o = !0, crossAxis: s = !1, limiter: c = { fn: (e) => {
					let { x: t, y: n } = e;
					return {
						x: t,
						y: n
					};
				} }, ...l } = $t(e, t), u = {
					x: n,
					y: r
				}, d = await a.detectOverflow(t, l), f = N(i), p = tn(f), m = u[p], h = u[f], g = (e, t) => Qt(t + d[e === "y" ? "top" : "left"], t, t - d[e === "y" ? "bottom" : "right"]);
				o && (m = g(p, m)), s && (h = g(f, h));
				let _ = c.fn({
					...t,
					[p]: m,
					[f]: h
				});
				return {
					..._,
					data: {
						x: _.x - n,
						y: _.y - r,
						enabled: {
							[p]: o,
							[f]: s
						}
					}
				};
			}
		};
	}, Mn = function(e) {
		return e === void 0 && (e = {}), {
			options: e,
			fn(t) {
				let { x: n, y: r, placement: i, rects: a, middlewareData: o } = t, { offset: s = 0, mainAxis: c = !0, crossAxis: l = !0 } = $t(e, t), u = {
					x: n,
					y: r
				}, d = N(i), f = tn(d), p = u[f], m = u[d], h = $t(s, t), g = typeof h == "number" ? {
					mainAxis: h,
					crossAxis: 0
				} : {
					mainAxis: h.mainAxis ?? 0,
					crossAxis: h.crossAxis ?? 0
				};
				if (c) {
					let e = f === "y" ? "height" : "width", t = a.reference[f] - a.floating[e] + g.mainAxis, n = a.reference[f] + a.reference[e] - g.mainAxis;
					p < t ? p = t : p > n && (p = n);
				}
				if (l) {
					let e = f === "y" ? "width" : "height", t = kn.has(M(i)), n = a.reference[d] - a.floating[e] + (t && o.offset?.[d] || 0) + (t ? 0 : g.crossAxis), r = a.reference[d] + a.reference[e] + (t ? 0 : o.offset?.[d] || 0) - (t ? g.crossAxis : 0);
					m < n ? m = n : m > r && (m = r);
				}
				return {
					[f]: p,
					[d]: m
				};
			}
		};
	}, Nn = function(e) {
		return e === void 0 && (e = {}), {
			name: "size",
			options: e,
			async fn(t) {
				let { placement: n, rects: r, platform: i, elements: a } = t, { apply: o = () => {}, ...s } = $t(e, t), c = await i.detectOverflow(t, s), l = M(n), u = en(n), d = N(n) === "y", { width: f, height: p } = r.floating, m, h;
				l === "top" || l === "bottom" ? (m = l, h = u === (await (i.isRTL == null ? void 0 : i.isRTL(a.floating)) ? "start" : "end") ? "left" : "right") : (h = l, m = u === "end" ? "top" : "bottom");
				let g = p - c.top - c.bottom, _ = f - c.left - c.right, v = mn(p - c[m], g), y = mn(f - c[h], _), b = t.middlewareData.shift, x = !b, S = v, C = y;
				b != null && b.enabled.x && (C = _), b != null && b.enabled.y && (S = g), x && !u && (d ? C = f - 2 * P(c.left, c.right) : S = p - 2 * P(c.top, c.bottom)), await o({
					...t,
					availableWidth: C,
					availableHeight: S
				});
				let w = await i.getDimensions(a.floating);
				return f !== w.width || p !== w.height ? { reset: { rects: !0 } } : {};
			}
		};
	};
}));
//#endregion
//#region node_modules/@floating-ui/utils/dist/floating-ui.utils.dom.mjs
function Fn() {
	return typeof window < "u";
}
function In(e) {
	return Ln(e) ? (e.nodeName || "").toLowerCase() : "#document";
}
function I(e) {
	var t;
	return (e == null || (t = e.ownerDocument) == null ? void 0 : t.defaultView) || window;
}
function L(e) {
	return ((Ln(e) ? e.ownerDocument : e.document) || window.document)?.documentElement;
}
function Ln(e) {
	return Fn() ? e instanceof Node || e instanceof I(e).Node : !1;
}
function R(e) {
	return Fn() ? e instanceof Element || e instanceof I(e).Element : !1;
}
function z(e) {
	return Fn() ? e instanceof HTMLElement || e instanceof I(e).HTMLElement : !1;
}
function Rn(e) {
	return !Fn() || typeof ShadowRoot > "u" ? !1 : e instanceof ShadowRoot || e instanceof I(e).ShadowRoot;
}
function zn(e) {
	let { overflow: t, overflowX: n, overflowY: r, display: i } = B(e);
	return /auto|scroll|overlay|hidden|clip/.test(t + r + n) && i !== "inline" && i !== "contents";
}
function Bn(e) {
	return /^(table|td|th)$/.test(In(e));
}
function Vn(e) {
	try {
		if (e.matches(":popover-open")) return !0;
	} catch {}
	try {
		return e.matches(":modal");
	} catch {
		return !1;
	}
}
function Hn(e) {
	let t = R(e) ? B(e) : e;
	return V(t.transform) || V(t.translate) || V(t.scale) || V(t.rotate) || V(t.perspective) || !Wn() && (V(t.backdropFilter) || V(t.filter)) || Zn.test(t.willChange || "") || Qn.test(t.contain || "");
}
function Un(e) {
	let t = qn(e);
	for (; z(t) && !Gn(t);) {
		if (Hn(t)) return t;
		if (Vn(t)) return null;
		t = qn(t);
	}
	return null;
}
function Wn() {
	return $n ??= typeof CSS < "u" && CSS.supports && CSS.supports("-webkit-backdrop-filter", "none"), $n;
}
function Gn(e) {
	return /^(html|body|#document)$/.test(In(e));
}
function B(e) {
	return I(e).getComputedStyle(e);
}
function Kn(e) {
	return R(e) ? {
		scrollLeft: e.scrollLeft,
		scrollTop: e.scrollTop
	} : {
		scrollLeft: e.scrollX,
		scrollTop: e.scrollY
	};
}
function qn(e) {
	if (In(e) === "html") return e;
	let t = e.assignedSlot || e.parentNode || Rn(e) && e.host || L(e);
	return Rn(t) ? t.host : t;
}
function Jn(e) {
	let t = qn(e);
	return Gn(t) ? (e.ownerDocument || e).body : z(t) && zn(t) ? t : Jn(t);
}
function Yn(e, t, n) {
	t === void 0 && (t = []), n === void 0 && (n = !0);
	let r = Jn(e), i = r === e.ownerDocument?.body, a = I(r);
	if (i) {
		let e = Xn(a);
		return t.concat(a, a.visualViewport || [], zn(r) ? r : [], e && n ? Yn(e) : []);
	}
	return t.concat(r, Yn(r, [], n));
}
function Xn(e) {
	return e.parent && Object.getPrototypeOf(e.parent) ? e.frameElement : null;
}
var Zn, Qn, V, $n, er = e((() => {
	Zn = /transform|translate|scale|rotate|perspective|filter/, Qn = /paint|layout|strict|content/, V = (e) => !!e && e !== "none";
}));
//#endregion
//#region node_modules/@floating-ui/dom/dist/floating-ui.dom.mjs
function tr(e) {
	let t = B(e), n = parseFloat(t.width) || 0, r = parseFloat(t.height) || 0, i = z(e), a = i ? e.offsetWidth : n, o = i ? e.offsetHeight : r, s = hn(n) !== a || hn(r) !== o;
	return s && (n = a, r = o), {
		width: n,
		height: r,
		$: s
	};
}
function nr(e) {
	return R(e) ? e : e.contextElement;
}
function rr(e) {
	let t = nr(e);
	if (!z(t)) return F(1);
	let n = t.getBoundingClientRect(), { width: r, height: i, $: a } = tr(t), o = (a ? hn(n.width) : n.width) / r, s = (a ? hn(n.height) : n.height) / i;
	return (!o || !Number.isFinite(o)) && (o = 1), (!s || !Number.isFinite(s)) && (s = 1), {
		x: o,
		y: s
	};
}
function ir(e) {
	let t = I(e);
	return !Wn() || !t.visualViewport ? Tr : {
		x: t.visualViewport.offsetLeft,
		y: t.visualViewport.offsetTop
	};
}
function ar(e, t, n) {
	return t === void 0 && (t = !1), !!n && t && n === I(e);
}
function H(e, t, n, r) {
	t === void 0 && (t = !1), n === void 0 && (n = !1);
	let i = e.getBoundingClientRect(), a = nr(e), o = F(1);
	t && (r ? R(r) && (o = rr(r)) : o = rr(e));
	let s = ar(a, n, r) ? ir(a) : F(0), c = (i.left + s.x) / o.x, l = (i.top + s.y) / o.y, u = i.width / o.x, d = i.height / o.y;
	if (a && r) {
		let e = I(a), t = R(r) ? I(r) : r, n = e, i = Xn(n);
		for (; i && t !== n;) {
			let e = rr(i), t = i.getBoundingClientRect(), r = B(i), a = t.left + (i.clientLeft + parseFloat(r.paddingLeft)) * e.x, o = t.top + (i.clientTop + parseFloat(r.paddingTop)) * e.y;
			c *= e.x, l *= e.y, u *= e.x, d *= e.y, c += a, l += o, n = I(i), i = Xn(n);
		}
	}
	return pn({
		width: u,
		height: d,
		x: c,
		y: l
	});
}
function or(e, t) {
	let n = Kn(e).scrollLeft;
	return t ? t.left + n : H(L(e)).left + n;
}
function sr(e, t) {
	let n = e.getBoundingClientRect();
	return {
		x: n.left + t.scrollLeft - or(e, n),
		y: n.top + t.scrollTop
	};
}
function cr(e) {
	let { elements: t, rect: n, offsetParent: r, strategy: i } = e, a = i === "fixed", o = L(r), s = t ? Vn(t.floating) : !1;
	if (r === o || s && a) return n;
	let c = {
		scrollLeft: 0,
		scrollTop: 0
	}, l = F(1), u = F(0), d = z(r);
	if ((d || !a) && ((In(r) !== "body" || zn(o)) && (c = Kn(r)), d)) {
		let e = H(r);
		l = rr(r), u.x = e.x + r.clientLeft, u.y = e.y + r.clientTop;
	}
	let f = o && !d && !a ? sr(o, c) : F(0);
	return {
		width: n.width * l.x,
		height: n.height * l.y,
		x: n.x * l.x - c.scrollLeft * l.x + u.x + f.x,
		y: n.y * l.y - c.scrollTop * l.y + u.y + f.y
	};
}
function lr(e) {
	return e.getClientRects ? Array.from(e.getClientRects()) : [];
}
function ur(e) {
	let t = Kn(e), n = e.ownerDocument.body, r = P(e.scrollWidth, e.clientWidth, n.scrollWidth, n.clientWidth), i = P(e.scrollHeight, e.clientHeight, n.scrollHeight, n.clientHeight), a = -t.scrollLeft + or(e), o = -t.scrollTop;
	return B(n).direction === "rtl" && (a += P(e.clientWidth, n.clientWidth) - r), {
		width: r,
		height: i,
		x: a,
		y: o
	};
}
function dr(e, t, n) {
	n === void 0 && (n = "viewport");
	let r = n === "layoutViewport", i = I(e), a = L(e), o = i.visualViewport, s = a.clientWidth, c = a.clientHeight, l = 0, u = 0;
	if (o) {
		let e = !Wn() || t === "fixed";
		r ? e || (l = -o.offsetLeft, u = -o.offsetTop) : (s = o.width, c = o.height, e && (l = o.offsetLeft, u = o.offsetTop));
	}
	if (or(a) <= 0) {
		let e = a.ownerDocument, t = e.body, n = getComputedStyle(t), r = e.compatMode === "CSS1Compat" && parseFloat(n.marginLeft) + parseFloat(n.marginRight) || 0, i = Math.abs(a.clientWidth - t.clientWidth - r), o = getComputedStyle(a).scrollbarGutter === "stable both-edges" ? i / 2 : i;
		o <= Er && (s -= o);
	}
	return {
		width: s,
		height: c,
		x: l,
		y: u
	};
}
function fr(e, t) {
	let n = H(e, !0, t === "fixed"), r = n.top + e.clientTop, i = n.left + e.clientLeft, a = rr(e);
	return {
		width: e.clientWidth * a.x,
		height: e.clientHeight * a.y,
		x: i * a.x,
		y: r * a.y
	};
}
function pr(e, t, n) {
	let r;
	if (t === "viewport" || t === "layoutViewport") r = dr(e, n, t);
	else if (t === "document") r = ur(L(e));
	else if (R(t)) r = fr(t, n);
	else {
		let n = ir(e);
		r = {
			x: t.x - n.x,
			y: t.y - n.y,
			width: t.width,
			height: t.height
		};
	}
	return pn(r);
}
function mr(e, t) {
	let n = t.get(e);
	if (n) return n;
	let r = Yn(e, [], !1).filter((e) => R(e) && In(e) !== "body"), i = null, a = B(e).position === "fixed", o = a ? qn(e) : e;
	for (; R(o) && !Gn(o);) {
		let e = B(o), t = Hn(o), n = i ? i.position : a ? "fixed" : "";
		!t && (n === "fixed" || n === "absolute" && e.position === "static") ? r = r.filter((e) => e !== o) : i = e, o = qn(o);
	}
	return t.set(e, r), r;
}
function hr(e) {
	let { element: t, boundary: n, rootBoundary: r, strategy: i } = e, a = [...n === "clippingAncestors" ? Vn(t) ? [] : mr(t, this._c) : [].concat(n), r], o = pr(t, a[0], i), s = o.top, c = o.right, l = o.bottom, u = o.left;
	for (let e = 1; e < a.length; e++) {
		let n = pr(t, a[e], i);
		s = P(n.top, s), c = mn(n.right, c), l = mn(n.bottom, l), u = P(n.left, u);
	}
	return {
		width: c - u,
		height: l - s,
		x: u,
		y: s
	};
}
function gr(e) {
	let { width: t, height: n } = tr(e);
	return {
		width: t,
		height: n
	};
}
function _r(e, t, n) {
	let r = z(t), i = L(t), a = n === "fixed", o = H(e, !0, a, t), s = {
		scrollLeft: 0,
		scrollTop: 0
	}, c = F(0);
	if ((r || !a) && ((In(t) !== "body" || zn(i)) && (s = Kn(t)), r)) {
		let e = H(t, !0, a, t);
		c.x = e.x + t.clientLeft, c.y = e.y + t.clientTop;
	}
	!r && i && (c.x = or(i));
	let l = i && !r && !a ? sr(i, s) : F(0);
	return {
		x: o.left + s.scrollLeft - c.x - l.x,
		y: o.top + s.scrollTop - c.y - l.y,
		width: o.width,
		height: o.height
	};
}
function vr(e) {
	return B(e).position === "static";
}
function yr(e, t) {
	if (!z(e) || B(e).position === "fixed") return null;
	if (t) return t(e);
	let n = e.offsetParent;
	return L(e) === n && (n = n.ownerDocument.body), n;
}
function br(e, t) {
	let n = I(e);
	if (Vn(e)) return n;
	if (!z(e)) {
		let t = qn(e);
		for (; t && !Gn(t);) {
			if (R(t) && !vr(t)) return t;
			t = qn(t);
		}
		return n;
	}
	let r = yr(e, t);
	for (; r && Bn(r) && vr(r);) r = yr(r, t);
	return r && Gn(r) && vr(r) && !Hn(r) ? n : r || Un(e) || n;
}
function xr(e) {
	return B(e).direction === "rtl";
}
function Sr(e, t) {
	return e.x === t.x && e.y === t.y && e.width === t.width && e.height === t.height;
}
function Cr(e, t, n) {
	let r = null, i, a = L(e);
	function o() {
		var e;
		clearTimeout(i), (e = r) == null || e.disconnect(), r = null;
	}
	function s(n, c) {
		n === void 0 && (n = !1), c === void 0 && (c = 1), o();
		let l = e.getBoundingClientRect(), { left: u, top: d, width: f, height: p } = l;
		if (n || t(), !f || !p) return;
		let m = gn(d), h = gn(a.clientWidth - (u + f)), g = gn(a.clientHeight - (d + p)), _ = gn(u), v = {
			rootMargin: -m + "px " + -h + "px " + -g + "px " + -_ + "px",
			threshold: P(0, mn(1, c)) || 1
		}, y = !0;
		function b(t) {
			let n = t[0].intersectionRatio;
			if (!Sr(l, e.getBoundingClientRect())) return s();
			if (n !== c) {
				if (!y) return s();
				n ? s(!1, n) : i = setTimeout(() => {
					s(!1, 1e-7);
				}, 1e3);
			}
			y = !1;
		}
		try {
			r = new IntersectionObserver(b, {
				...v,
				root: a.ownerDocument
			});
		} catch {
			r = new IntersectionObserver(b, v);
		}
		r.observe(e);
	}
	let c = I(e), l = () => s(n);
	return c.addEventListener("resize", l), s(!0), () => {
		c.removeEventListener("resize", l), o();
	};
}
function wr(e, t, n, r) {
	r === void 0 && (r = {});
	let { ancestorScroll: i = !0, ancestorResize: a = !0, elementResize: o = typeof ResizeObserver == "function", layoutShift: s = typeof IntersectionObserver == "function", animationFrame: c = !1 } = r, l = nr(e), u = i || a ? [...l ? Yn(l) : [], ...t ? Yn(t) : []] : [];
	u.forEach((e) => {
		i && e.addEventListener("scroll", n), a && e.addEventListener("resize", n);
	});
	let d = l && s ? Cr(l, n, a) : null, f = -1, p = null;
	o && (p = new ResizeObserver((e) => {
		let [r] = e;
		r && r.target === l && p && t && (p.unobserve(t), cancelAnimationFrame(f), f = requestAnimationFrame(() => {
			var e;
			(e = p) == null || e.observe(t);
		})), n();
	}), l && !c && p.observe(l), t && p.observe(t));
	let m, h = c ? H(e) : null;
	c && g();
	function g() {
		let t = H(e);
		h && !Sr(h, t) && n(), h = t, m = requestAnimationFrame(g);
	}
	return n(), () => {
		var e;
		u.forEach((e) => {
			i && e.removeEventListener("scroll", n), a && e.removeEventListener("resize", n);
		}), d?.(), (e = p) == null || e.disconnect(), p = null, c && cancelAnimationFrame(m);
	};
}
var Tr, Er, Dr, Or, kr, Ar, jr, Mr, Nr, Pr, Fr = e((() => {
	Pn(), Sn(), er(), Tr = /*#__PURE__*/ F(0), Er = 25, Dr = async function(e) {
		let t = this.getOffsetParent || br, n = this.getDimensions, r = await n(e.floating);
		return {
			reference: _r(e.reference, await t(e.floating), e.strategy),
			floating: {
				x: 0,
				y: 0,
				width: r.width,
				height: r.height
			}
		};
	}, Or = {
		convertOffsetParentRelativeRectToViewportRelativeRect: cr,
		getDocumentElement: L,
		getClippingRect: hr,
		getOffsetParent: br,
		getElementRects: Dr,
		getClientRects: lr,
		getDimensions: gr,
		getScale: rr,
		isElement: R,
		isRTL: xr
	}, kr = An, Ar = jn, jr = On, Mr = Nn, Nr = Mn, Pr = (e, t, n) => {
		let r = /* @__PURE__ */ new Map(), i = n ?? {}, a = {
			...Or,
			...i.platform,
			_c: r
		};
		return Dn(e, t, {
			...i,
			platform: a
		});
	};
}));
//#endregion
//#region node_modules/@digdir/designsystemet-web/dist/esm/popover/popover.js
function Ir(e, t, n, r) {
	let i = e instanceof HTMLElement && m(e, "popover") !== null && h(e, "--_ds-floating"), a = zr.get(e);
	if (t === "open" && a && a.source === r || (a?.cleanup(), t === "closed" || !i)) return;
	if (!r) {
		let t = e.id && `[popovertarget="${e.id}"],[commandfor="${e.id}"]`;
		r = t && v(e).querySelector(t) || void 0;
	}
	if (!r || r === e || n && n === t) return;
	e.style.scrollMarginBottom = "var(--_ds-floating-arrow-size)";
	let o = h(e, "--_ds-floating-overscroll"), s = m(e, Lr) || m(r, Lr) || i, c = m(e, Rr) || m(r, Rr), l = parseFloat(h(e, "scroll-margin-bottom")) || 0, u = s.match(/left|right/gi) ? "Height" : "Width", d = r[`offset${u}`] / 2 + l;
	if (s === "none") return;
	let f = !1, p = {
		strategy: "absolute",
		placement: s,
		middleware: [
			kr(l),
			Ar({
				padding: 10,
				limiter: Nr({ offset: { mainAxis: d } })
			}),
			Wr(),
			...c === "false" ? [] : [jr({
				padding: 10,
				crossAxis: !1
			})],
			...o ? [Mr({ apply({ availableHeight: t }) {
				if (f) return;
				f = !0;
				let n = `${r.offsetWidth}px`, i = `${Math.max(50, t - 20)}px`;
				requestAnimationFrame(() => {
					o === "fit" && e.style.width !== n && (e.style.width = n), e.style.maxHeight !== i && (e.style.maxHeight = i);
				});
			} })] : []
		]
	}, g = wr(r, e, async () => {
		if (!r?.isConnected) return zr.get(e)?.cleanup();
		let { x: t, y: n } = await Pr(r, e, p);
		e.style.translate = `${t}px ${n}px`;
	});
	zr.set(e, {
		source: r,
		cleanup: () => zr.delete(e) && g()
	});
}
var U, Lr, Rr, zr, Br, Vr, Hr, Ur, Wr, Gr = e((() => {
	O(), Zt(), Fr(), u() && !At() && !jt() && It({ layerName: "ds.base" }), U = /* @__PURE__ */ new Map(), Lr = "data-placement", Rr = "data-autoplacement", zr = /* @__PURE__ */ new Map(), Br = (e) => Ir(y(e), e.newState, e.oldState, e.source || e.detail), Hr = (e) => {
		if (e.type === "pointerdown" && (Vr = !1), e.type === "scroll" && Vr === !1 && (Vr = !0), e.type === "pointerup" && Vr) for (let [e] of zr) e.showPopover();
	}, Ur = (e) => {
		for (let [e, t] of U) !e.host?.isConnected && U.delete(e) && t();
		let t = v(y(e));
		t instanceof ShadowRoot && !U.has(t) && U.set(t, x(t, "toggle", Br, l));
	}, w("popover", () => {
		let e = Object.getOwnPropertyDescriptors(HTMLElement.prototype), t = HTMLElement.prototype.togglePopover, n = HTMLElement.prototype.showPopover, r = HTMLElement.prototype.hidePopover;
		return Object.defineProperties(HTMLElement.prototype, {
			togglePopover: {
				...e.togglePopover,
				value(e) {
					let n = this.matches(":popover-open"), r = typeof e == "boolean", i = n ? "open" : "closed", a = (r ? e : e?.force ?? !n) ? "open" : "closed", o = r ? void 0 : e?.source, s = t?.call(this, e);
					return Ir(this, a, i, o), s;
				}
			},
			showPopover: {
				...e.showPopover,
				value(e) {
					let t = this.matches(":popover-open") ? "open" : "closed", r = n?.call(this, e);
					return Ir(this, "open", t, e?.source), r;
				}
			},
			hidePopover: {
				...e.hidePopover,
				value() {
					let e = this.matches(":popover-open") ? "open" : "closed", t = r?.call(this);
					return Ir(this, "closed", e), t;
				}
			}
		}), [
			x(document, "click", Ur, l),
			x(document, "pointerdown pointerup scroll", Hr, l),
			x(document, "toggle ds-toggle-source", Br, l),
			() => {
				Object.defineProperties(HTMLElement.prototype, {
					togglePopover: {
						...e.togglePopover,
						value: t
					},
					showPopover: {
						...e.showPopover,
						value: n
					},
					hidePopover: {
						...e.hidePopover,
						value: r
					}
				});
				for (let [, e] of U) e();
				U.clear();
			}
		];
	}), Wr = () => ({
		name: "arrowPseudo",
		fn(e) {
			let t = e.elements.floating, n = e.rects.reference, r = `${Math.round(n.width / 2 + n.x - e.x)}px`, i = `${Math.round(n.height / 2 + n.y - e.y)}px`;
			return t.style.setProperty("--_ds-floating-arrow-x", r), t.style.setProperty("--_ds-floating-arrow-y", i), m(t, "data-floating", e.placement), e;
		}
	});
})), Kr, qr, Jr, Yr, Xr = e((() => {
	O(), Kr = (e) => m(e, "readonly") !== null || m(e, "aria-readonly") === "true", qr = (e) => {
		(!(e.key === "Tab" || e.altKey || e.ctrlKey || e.metaKey) || e.key?.startsWith("Arrow")) && Jr(e);
	}, Jr = (e) => {
		let t = y(e);
		t?.nodeName === "SELECT" && Kr(t) && e.preventDefault();
	}, Yr = (e) => {
		for (let t of e.composedPath()) if (t?.nodeName === "LABEL" && (t = t.control), t?.nodeName === "INPUT" || t?.nodeName === "SELECT") {
			Kr(t) && (e.stopImmediatePropagation(), e.preventDefault(), t[t.nodeName === "SELECT" ? "blur" : "focus"](), requestAnimationFrame(() => t.isConnected && t.focus()));
			return;
		}
	}, w("readonly", () => [
		x(document, "keydown", qr),
		x(document, "click", Yr, !0),
		x(document, "mousedown", Jr, !0)
	]);
})), Zr, Qr, $r, ei, ti, ni, ri, ii, ai, oi, si = e((() => {
	O(), Zr = "focusgroup", Qr = "data-toggle-group", $r = `[${Qr}]`, ti = (e, t) => {
		for (let n of t || [null]) if (n?.attributeName === Qr) oi(n.target);
		else if (!n || n.addedNodes.length) {
			let t = n?.target || e;
			for (let e of t.querySelectorAll($r)) oi(e);
		}
	}, ni = (e) => {
		ei && ai(e) && (e.preventDefault(), e.stopImmediatePropagation());
	}, ri = () => ei = !1, ii = (e) => {
		ei = e.key?.startsWith("Arrow"), e.key === "Enter" && ai(e)?.click(), ei && setTimeout(ri, 0);
	}, ai = (e) => {
		let t = y(e);
		if (!(t?.nodeName !== "INPUT" || t.type !== "radio" || !t.name)) {
			for (let e of b(t)) if (!(e.nodeType !== 1 || !e.hasAttribute(Zr))) return m(e, Zr)?.includes("radiogroup") ? t : void 0;
		}
	}, oi = (e) => {
		if (e.hasAttribute(Zr)) return;
		let t = _(e, Qr), n = m(e, c)?.trim(), r = `Please use ${Zr}="radiogroup" and ${n ? c : s}="${n || t}" instead of deprecated ${Qr} on:`;
		m(e, s, n ? null : t), m(e, Zr, "radiogroup"), p(r, e);
	}, w("toggle-group", () => [
		x(document, "click", ni, !0),
		x(document, "keydown", ii),
		T(document, ti, {
			attributeFilter: [Qr],
			attributes: !0,
			childList: !0,
			subtree: !0
		})
	]);
})), W, ci, li, G, ui, di, fi, pi, mi, hi, gi, _i, vi, yi, bi, xi, Si, Ci, wi, Ti, Ei = e((() => {
	O(), Gr(), ui = 0, di = "data-color", fi = "data-color-scheme", pi = "data-size", mi = "data-tooltip", hi = u() && /iPad|iPhone|iPod/.test(navigator.userAgent), gi = `[${di}]`, _i = `[${fi}]`, vi = `[${pi}]`, yi = `[${mi}]`, bi = (e = document) => {
		for (let t of e?.querySelectorAll(yi) || []) Si(t);
	}, xi = (e, t) => {
		if (!t) return bi();
		for (let e of t) if (e.target !== W) {
			if (e.attributeName === mi) Si(e.target);
			else for (let t of e.addedNodes) t.nodeType === 1 && (t.hasAttribute(mi) ? Si(t) : bi(t));
		}
	}, Si = (e, t = !0) => {
		let n = _(e, mi) || "";
		if (n[0] === "#" && (n = v(e).getElementById(n.slice(1))?.textContent?.trim() || ""), n !== (e.getAttribute("aria-label") || e.getAttribute("aria-description"))) {
			let t = m(e, "role") !== "img" && e.textContent?.trim();
			m(e, mi, n), m(e, s, t ? null : n), m(e, o, t ? n : null), e.tabIndex === -1 && p("Missing tabindex=\"0\" attribute on: ", e);
		}
		e === G && W?.textContent !== n && (W && (W.textContent = n), n && t && document.activeElement === e && ie(n));
	}, Ci = (e) => {
		let t = y(e);
		if (!t || li === t || W?.contains(t)) return;
		li = t;
		let n = li?.closest?.(yi) || void 0;
		if (G !== n && (G && Ti(), G = n, n)) {
			if (e.type === "focus" || hi || 300 > Date.now() - ui) return wi();
			e.type === "mousemove" && (ci = setTimeout(wi, 300));
		}
	}, wi = () => {
		if (!G) return Ti();
		W ||= ee("div", { class: "ds-tooltip" }), W.isConnected || document.body.appendChild(W);
		let e = G.closest(gi), t = G.closest(_i), n = G.closest(vi), r = e !== t && e?.contains(t);
		m(W, "popover", "manual"), m(W, fi, t?.getAttribute(fi) || null), m(W, di, r && e?.getAttribute(di) || null), m(W, pi, n?.getAttribute(pi) || null), Si(G, !1), W.showPopover(), W.dispatchEvent(new CustomEvent("ds-toggle-source", {
			bubbles: !0,
			composed: !0,
			detail: G
		}));
	}, Ti = (e) => {
		(e?.type !== "keydown" || e?.key === "Escape") && (G && W?.isConnected && W.popover && W.hidePopover(), e || (ui = Date.now()), clearTimeout(ci), G = void 0);
	}, w("tooltip", () => [
		x(document, "focus mousemove", Ci, l),
		x(document, "keydown", Ti, l),
		T(document, xi, {
			attributeFilter: [mi],
			attributes: !0,
			childList: !0,
			subtree: !0
		})
	]);
})), Di, Oi, ki = e((() => {
	O(), Di = class extends f {
		_items;
		_label = {
			key: s,
			value: null
		};
		_unresize;
		_unmutate;
		static get observedAttributes() {
			return [s, c];
		}
		connectedCallback() {
			let e = i(() => Oi(this), 100);
			this._items = this.getElementsByTagName("a"), this._unresize = x(window, "resize", e), this._unmutate = T(this, Oi, {
				childList: !0,
				subtree: !0
			});
		}
		attributeChangedCallback() {
			this._unmutate && Oi(this);
		}
		disconnectedCallback() {
			this._unresize?.(), this._unmutate?.(), this._unresize = this._unmutate = this._items = void 0;
		}
	}, Oi = (e) => {
		let t = e._items?.[e._items.length - 1], n = t?.parentElement === e ? null : t, r = !n?.offsetHeight;
		m(e, "role", r ? null : "navigation");
		for (let t of e._items || []) m(t, "aria-current", t === n ? "page" : null);
		let i = _(e, s)?.trim(), a = m(e, c)?.trim(), o = i || a;
		o && (e._label.value = o), o && (e._label.key = a ? c : s);
		let l = r ? null : e._label.value;
		o !== l && (m(e, c, null), m(e, s, null), m(e, e._label.key, l));
	}, E.define("ds-breadcrumbs", Di);
})), Ai, ji, Mi = e((() => {
	O(), Ai = class extends f {
		_unmutate;
		connectedCallback() {
			x(this, "animationend", this, l), m(this, "role", "group"), m(this, "tabindex", "-1"), this._unmutate = T(this, ji, {
				childList: !0,
				subtree: !0
			}), this.focus();
		}
		handleEvent({ target: e }) {
			e === this && this.focus();
		}
		disconnectedCallback() {
			S(this, "animationend", this, l), this._unmutate?.(), this._unmutate = void 0;
		}
	}, ji = (e) => {
		let t = m(e, s)?.trim(), n = m(e, c)?.trim(), r = e.querySelector("h2,h3,h4,h5,h6");
		r && !t && !n && m(e, c, a(r)), !r && !t && !n && p("Missing accessible name on:", e, `\nAdd a heading (h2–h6), or set ${s} or ${c} to provide an accessible name for screen readers.`);
	}, E.define("ds-error-summary", Ai);
})), Ni, Pi, Fi, Ii, Li, Ri, zi, Bi, Vi, Hi, Ui, Wi, Gi, Ki = e((() => {
	O(), Ni = "data-field", Pi = "data-limit", Fi = "aria-invalid", Ii = "aria-describedby", Li = "data-indeterminate", Ri = `:scope > [${Ni}="validation"]`, zi = d() ? 800 : 200, Bi = (e) => {
		let t = [], n = [], r = e._descs || [], i = !1, o = !1;
		e._input?.hidden && (e._input = void 0);
		for (let r of e.getElementsByTagName("*")) if (r instanceof HTMLLabelElement && t.push(r), !r.hidden) {
			if (Wi(r)) e._input?.isConnected && e._input !== r ? p("Fields should only have one input element. Use <fieldset> to group multiple fields:", e) : e._input = r;
			else {
				let t = r.getAttribute(Ni);
				t === "counter" && (e._counter = r), t === "validation" ? (n.unshift(a(r)), i = !0, o ||= Ui(r)) : t && n.push(a(r));
			}
		}
		if (!e._input?.isConnected) return;
		let s = e._input;
		for (let e of t) m(e, "for", a(s) || null);
		let c = e.closest("fieldset")?.querySelector(Ri);
		c && !c?.hidden && (i = !0, o ||= Ui(c), n.unshift(a(c)));
		let l = m(s, Li);
		l && (s.indeterminate = l === "true"), (s.type === "radio" || s.type === "checkbox") && m(e, "data-clickdelegatefor", a(s));
		let u = (m(s, Ii)?.trim().split(/\s+/))?.filter((e) => !r.includes(e)) || [];
		m(s, Ii, [...n, ...u].join(" ") || null), e._descs = n;
		let d = e._validation;
		i && !d && !s.hasAttribute(Fi) ? (m(s, Fi, o ? "true" : null), e._validation = !0) : !i && d && (m(s, Fi, null), e._validation = void 0), e.handleEvent();
	}, Vi = {
		over: "%d tegn for mye",
		under: "%d tegn igjen"
	}, Hi = i((e, t) => {
		document?.activeElement === e && ie(t);
	}, zi), Ui = (e) => {
		let t = e.getAttribute("data-color");
		return !t || t === "danger";
	}, Wi = (e) => e instanceof HTMLElement && "validity" in e && !(e instanceof HTMLButtonElement) && e.type !== "hidden", Gi = class extends f {
		_counter;
		_descs;
		_input;
		_validation;
		_unevents;
		_unmutate;
		connectedCallback() {
			this._unevents = x(this, "input", this, l), this._unmutate = T(this, () => Bi(this), {
				attributeFilter: [
					Ni,
					Pi,
					"hidden",
					"id",
					"value",
					Li
				],
				attributes: !0,
				childList: !0,
				subtree: !0
			});
		}
		handleEvent(e) {
			let { _counter: t, _input: n } = this;
			if (t?.isConnected && n) {
				let r = (Number(m(t, Pi)) || 0) - n.value.length, i = r < 0 ? "over" : "under", a = (_(t, `data-${i}`) || Vi[i])?.replace("%d", `${Math.abs(r)}`);
				a || p(`Missing data-${i} on:`, t), m(t, "data-label", a), m(t, "data-state", i), m(t, "data-color", r < 0 ? "danger" : null), m(n, "aria-invalid", r < 0 ? "true" : null), e?.type === "input" && a && Hi(n, a);
			}
			n instanceof HTMLTextAreaElement && (n.style.setProperty("--_ds-field-sizing", "auto"), n.style.setProperty("--_ds-field-sizing", `${n.scrollHeight}px`));
		}
		disconnectedCallback() {
			this._unevents?.(), this._unmutate?.(), this._unevents = this._unmutate = void 0, this._input = this._counter = this._descs = this._validation = void 0;
		}
	}, E.define("ds-field", Gi);
})), qi, Ji, Yi, Xi, Zi, Qi, $i, ea = e((() => {
	O(), qi = "data-current", Ji = "data-total", Yi = "data-href", Xi = ({ current: e = 1, total: t = 10, show: n = 7 }) => ({
		prev: e > 1 ? e - 1 : 0,
		next: e < t ? e + 1 : 0,
		pages: $i(e, t, n).map((t, n) => ({
			current: t === e && "page",
			key: `key-${t}-${n}`,
			page: t
		}))
	}), Zi = class extends f {
		_unmutate;
		_render;
		static get observedAttributes() {
			return [
				s,
				c,
				qi,
				Ji,
				Yi
			];
		}
		connectedCallback() {
			let e = m(this, Ji), t = m(this, qi);
			t && !e && p(`Missing ${Ji} attribute on:`, this), e && !t && p(`Missing ${qi} attribute on:`, this);
			let n = _(this, s), r = m(this, c)?.trim();
			n || r ? m(this, s, r ? null : n) : p(`Missing ${s} on:`, this), m(this, "role", "navigation"), this._unmutate = T(this, Qi, {
				childList: !0,
				subtree: !0
			});
		}
		attributeChangedCallback() {
			this._unmutate && Qi(this);
		}
		disconnectedCallback() {
			this._unmutate?.(), this._unmutate = this._render = void 0;
		}
	}, Qi = (e) => {
		let t = Number(m(e, qi)), n = Number(m(e, Ji));
		if (t && n) {
			let r = e.querySelectorAll("button,a"), i = r.length - 2, a = m(e, Yi), { next: o, prev: c, pages: l } = Xi({
				current: t,
				total: n,
				show: i
			});
			r.forEach((e, t) => {
				let n = t ? r[t + 1] ? l[t - 1]?.page : o : c;
				m(e, "aria-current", l[t - 1]?.current ? "true" : null), m(e, s, `${n ?? "hidden"}`), m(e, "role", n ? null : "none"), m(e, "tabindex", n ? null : "-1"), e instanceof HTMLButtonElement && m(e, "value", `${n}`), a && e instanceof HTMLAnchorElement && m(e, "href", a.replace("%d", `${n}`));
			});
		}
	}, $i = (e, t, n = 1 / 0) => {
		let r = (n - 1) / 2, i = Math.max(Math.min(e - Math.floor(r), t - n + 1), 1), a = Math.min(Math.max(e + Math.ceil(r), n), t), o = Array.from({ length: a + 1 - i }, (e, t) => t + i);
		return n > 4 && i > 1 && o.splice(0, 2, 1, 0), n > 3 && a < t && o.splice(-2, 2, 0, t), o;
	}, E.define("ds-pagination", Zi);
})), ta, na, ra, ia, aa, oa, sa, ca, la, ua, da, fa, pa, ma, ha, ga, _a, va, ya, K, ba, xa, Sa, Ca, wa, Ta, Ea, Da, Oa, ka, Aa, ja, Ma, Na, Pa, Fa, Ia, q, J, La, Ra, za, Ba, Va, Y, Ha, Ua, Wa, Ga, Ka, qa, Ja, Ya, Xa, Za, Qa, $a, eo, to, no, ro, io, ao, oo, so, co, lo, uo, fo, po, mo, ho, go, _o = e((() => {
	ta = Object.defineProperty, na = Object.getOwnPropertySymbols, ra = Object.prototype.hasOwnProperty, ia = Object.prototype.propertyIsEnumerable, aa = (e, t, n) => t in e ? ta(e, t, {
		enumerable: !0,
		configurable: !0,
		writable: !0,
		value: n
	}) : e[t] = n, oa = (e, t) => {
		for (var n in t ||= {}) ra.call(t, n) && aa(e, n, t[n]);
		if (na) for (var n of na(t)) ia.call(t, n) && aa(e, n, t[n]);
		return e;
	}, sa = () => typeof window < "u" && window.document !== void 0 && window.navigator !== void 0, ca = sa(), la = ca ? navigator.userAgent : "", ua = /android/i.test(la), da = /firefox/i.test(la), fa = /iPad|iPhone|iPod/.test(la), pa = ca && /^Mac/i.test(navigator.userAgentData?.platform || navigator.platform), ma = ca && window.CSSStyleSheet && document.adoptedStyleSheets, ha = {
		once: !0,
		capture: !0,
		passive: !0
	}, ga = ":host(:not([hidden])) { display: block }", _a = "outline: 1px dotted; outline: 5px auto Highlight; outline: 5px auto -webkit-focus-ring-color", va = `${ua ? "data" : "aria"}-multiselectable`, ya = typeof HTMLElement > "u" ? class {} : HTMLElement, K = (e, t, n) => n === void 0 ? e.getAttribute(t) : (n === null ? e.removeAttribute(t) : e.getAttribute(t) !== n && e.setAttribute(t, n), null), ba = (e, ...t) => {
		let [n, ...r] = t;
		for (let t of n.split(" ")) e.addEventListener(t, ...r);
		return () => xa(e, ...t);
	}, xa = (e, ...t) => {
		let [n, ...r] = t;
		for (let t of n.split(" ")) e.removeEventListener(t, ...r);
	}, Sa = (e, t) => {
		let n = e.shadowRoot || e.attachShadow({ mode: "open" });
		if (n.querySelector("slot") || n.appendChild(ka("slot")), !n.querySelector("style")) {
			if (!ma) n.appendChild(ka("style", null, t));
			else {
				let e = new CSSStyleSheet();
				e.replaceSync(t), n.adoptedStyleSheets = [e];
			}
		}
		return n;
	}, Ca = (e, t, n) => {
		let r = new MutationObserver((n) => {
			if (!sa() || !e.isConnected) return i();
			t(e, n);
		}), i = Object.assign(() => r.disconnect(), { takeRecords: () => r.takeRecords() });
		return t(e), r.observe(e, n), i;
	}, wa = (e) => {
		let t = e.getRootNode?.call(e) || e.ownerDocument;
		return t instanceof Document || t instanceof ShadowRoot ? t : document;
	}, Ta = (e) => wa(e).activeElement, Ea = (e) => {
		let t = K(e, "aria-label") || "";
		return [...(K(e, "aria-labelledby")?.split(" ") || []).map((e) => document.getElementById(e.trim() || "-")), ...Array.from(e.labels || [])].reduce((e, t) => e || (t?.innerText)?.trim() || "", t).trim();
	}, Da = (e) => {
		if (!e || !ca) return null;
		if (window.uElementsId || (window.uElementsId = {}), !e.id) {
			let t = e.nodeName.toLowerCase();
			window.uElementsId[t] || (window.uElementsId[t] = 1), e.id = `:${t}${window.uElementsId[t]++}`;
		}
		return e.id;
	}, Oa = ca ? Object.prototype.hasOwnProperty : () => !1, ka = (e, t, n) => {
		let r = document.createElement(e);
		if (n && (r.textContent = n), t) for (let e in t) Oa.call(t, e) && K(r, e, t[e]);
		return r;
	}, Aa = { define: (e, t) => !sa() || window.customElements.get(e) || window.customElements.define(e, t) }, ja = (e, t, n = "") => {
		var r;
		let i = {
			bubbles: !0,
			composed: !0,
			data: t,
			inputType: n
		}, a = HTMLInputElement.prototype;
		e.dispatchEvent(new InputEvent("beforeinput", i)), (r = Object.getOwnPropertyDescriptor(a, "value")?.set) == null || r.call(e, t), e.dispatchEvent(new InputEvent("input", i)), e.dispatchEvent(new Event("change", { bubbles: !0 }));
	}, Ma = /* @__PURE__ */ new WeakSet(), Na = (e, t) => (t?.type === "pointerdown" && (Ma.add(e), ba(document, "pointerup", () => Ma.delete(e), ha)), Ma.has(e)), Pa = (e, t = "<slot></slot>") => `<template shadowrootmode="open">${t}<style>${e}</style></template>`, Fa = (e) => {
		let t = K(e, "form");
		K(e, "form", "#"), setTimeout(Ia, 0, e, t);
	}, Ia = (e, t) => K(e, "form", t), q = (e) => (e?.textContent)?.trim() || "", La = 0, Ra = 0, za = (e) => {
		clearTimeout(Ra), J || (J = ka("div", { "aria-live": "assertive" }), J.style.overflow = "hidden", J.style.position = "fixed", J.style.whiteSpace = "nowrap", J.style.width = "1px"), J.isConnected || document.body.appendChild(J), e === "" && (J.textContent = ""), e && (J.textContent = `${e}${La++ % 2 ? "\xA0" : ""}`, Ra = setTimeout(za, !pa && da ? 2e3 : 300, ""));
	}, Ba = `${ga}
[part="items"] { display: inline-flex; flex-wrap: wrap } /* Can not be "contents" as this confuses VoiceOver */
:host(:not([data-multiple])) [part="items"],
:host([data-multiple="false"]) [part="items"] { display: none }
::slotted(button[type="reset"]),
::slotted(button[aria-expanded]),
::slotted(del) { font: inherit; border: 0; padding: 0; background: none; color: inherit; cursor: pointer; text-decoration: none }
::slotted(data) { cursor: pointer; pointer-events: none }
::slotted(data)::after { padding-inline: .5ch; pointer-events: auto }
::slotted(data)::after,
::slotted(del:empty)::before,
::slotted(button[type="reset"]:empty)::before { content: '\\00D7'; content: '\\00D7' / '' }
::slotted(button[aria-expanded="false"]:empty)::before { content: '\\25BC'; content: '\\25BC' / '' }
::slotted(button[aria-expanded="true"]:empty)::before { content: '\\25B2'; content: '\\25B2' / '' }
::slotted(data:focus),::slotted(del:focus),::slotted(button[type="reset"]:focus) { ${_a} }`, Pa(Ba), Va = !1, Y = "aria-label", Ha = "button[type=\"reset\"],del", Ua = "button[aria-expanded]", Wa = "datalist,u-datalist,[role=\"listbox\"]", Ga = "option,u-option,[role=\"option\"]", Ka = "blur focus click input keydown pointerdown", qa = "false", Ja = {
		added: "Added",
		clear: "Clear input",
		empty: "No selected",
		found: "Navigate left to find %d selected",
		invalid: "Invalid value",
		items: "Selected",
		of: "of",
		remove: "Press to remove",
		removed: "Removed",
		toggle: "Options"
	}, Ya = class extends ya {
		constructor() {
			super(), this._focusMoved = !1, this._itemSingleVale = "", this._speak = "", this._texts = oa({}, Ja), this._value = "";
			let e = Sa(this, Ba);
			this._listbox = e.querySelector("[role=\"listbox\"]") || ka("div"), this._listbox.innerHTML = "<slot name=\"items\"></slot>", K(this._listbox, "aria-orientation", "horizontal"), K(this._listbox, "role", "listbox"), K(this._listbox, "part", "items"), K(this._listbox, "tabindex", "-1"), e.insertBefore(this._listbox, e.firstChild);
		}
		static get observedAttributes() {
			return Object.keys(Ja).map((e) => `data-sr-${e}`);
		}
		connectedCallback() {
			ba(this, Ka, this, !0), this._umutate = Ca(this, ao, {
				attributeFilter: [
					"id",
					"value",
					"role",
					"aria-expanded"
				],
				attributes: !0,
				characterData: !0,
				childList: !0,
				subtree: !0
			}), fo(this), lo(this);
		}
		attributeChangedCallback(e, t, n) {
			let r = e.split("data-sr-")[1];
			Ja[r] && (this._texts[r] = n || Ja[r]), r === "clear" && this.clear && K(this.clear, Y, this._texts.clear);
		}
		disconnectedCallback() {
			var e;
			xa(this, Ka, this, !0), (e = this._umutate) == null || e.call(this), this._umutate = this._clear = this._toggle = void 0, this._control = this._match = this._select = void 0, this._options = this._items = this._list = void 0;
		}
		handleEvent(e) {
			this.control?.disabled || this.control?.readOnly || (e.type === "blur" && Qa(this), e.type === "click" && eo(this, e), e.type === "focus" && za(), e.type === "input" && to(this, e), e.type === "keydown" && no(this, e), e.type === "pointerdown" && (Va = !!this.list?.hidden, Na(this, e)));
		}
		get multiple() {
			return (K(this, "data-multiple") ?? qa) !== qa;
		}
		set multiple(e) {
			K(this, "data-multiple", e ? "" : null);
		}
		get creatable() {
			return (K(this, "data-creatable") ?? qa) !== qa;
		}
		set creatable(e) {
			K(this, "data-creatable", e ? "" : null);
		}
		get control() {
			return this._control?.isConnected || (this._control = this.querySelector("input")), this._control;
		}
		get list() {
			return this._list?.isConnected || (this._list = this.querySelector(Wa), this._options = void 0), this._list;
		}
		get clear() {
			return this._clear?.isConnected || (this._clear = this.querySelector(Ha)), this._clear;
		}
		get toggle() {
			return this._toggle?.isConnected || (this._toggle = this.querySelector(Ua)), this._toggle;
		}
		get items() {
			return this._items ||= this.getElementsByTagName("data"), this._items;
		}
		get options() {
			let e = !this._options && this.list?.querySelector(Ga)?.nodeName;
			return e && (this._options = this.list?.getElementsByTagName(e)), this._options || this.getElementsByTagName("-");
		}
		get values() {
			return Array.from(this.items, ({ value: e }) => e);
		}
	}, Xa = (e) => {
		let { creatable: t, control: n, options: r, multiple: i, list: a } = e, o = (n?.value)?.trim() || "", s = o.toLowerCase() || null, c;
		if (a) {
			c = [...r].find((e) => po(e).trim().toLowerCase() === s);
			let t = {
				bubbles: !0,
				cancelable: !0,
				detail: c
			};
			if (e.dispatchEvent(new CustomEvent("comboboxbeforematch", t)) || (c = [...r].find(go)), i) uo(e);
			else for (let e of r) ho(e, e === c);
		}
		return !c && t && o ? {
			value: o,
			label: o
		} : c && {
			value: mo(c),
			label: po(c)
		};
	}, Za = (e, t, n = !0) => {
		let { _texts: r, control: i, items: a, multiple: o } = e;
		if (!t) return o ? za(r.invalid) : !i?.value && a[0] ? Za(e, a[0]) : fo(e);
		let s = [...a].findIndex((e) => e.value === t.value), c = a[s], l = c === Ta(e) && (o && (a[s - 1] || a[s + 1]) || i);
		if (c && !n) return fo(e);
		l && l.focus(), e._focusMoved = !!l;
		let u = ka("data", { value: t.value }, t.label || t.value), d = {
			bubbles: !0,
			cancelable: !0,
			detail: c || u
		};
		if (e.dispatchEvent(new CustomEvent("comboboxbeforeselect", d))) {
			if (!o) for (let e of [...a]) e.remove();
			c ? c.remove() : i?.insertAdjacentElement("beforebegin", u), e.dispatchEvent(new CustomEvent("comboboxafterselect", d));
		}
	}, Qa = (e) => Na(e) || setTimeout($a, 0, e), $a = (e) => e.multiple || e.contains(Ta(e)) || Za(e, e._match, !1), eo = (e, t) => {
		let { clientX: n, clientY: r, target: i } = t, { clear: a, control: o, items: s, toggle: c } = e, l = i === e;
		if (c?.contains(i) && (o?.focus(), Va && o?.click()), o && a?.contains(i)) return t.preventDefault(), ja(o, "", "deleteContentBackward"), o.focus(), Va || o.click();
		for (let t of s) {
			if (t.contains(i)) return Za(e, t);
			if (!l) continue;
			let { top: a, right: o, bottom: s, left: c, width: u, height: d } = t.getBoundingClientRect();
			if (u && d && r >= a && r <= s && n >= c && n <= o) return t.focus();
		}
		l && o?.focus();
	}, to = (e, t) => {
		var n;
		let { control: r, options: i, multiple: a } = e, o = r?.value || null;
		if (!(t instanceof InputEvent ? !t.inputType || t.inputType === "insertReplacementText" : r?.value)) e._value = r?.value || "", a || (e._match = Xa(e));
		else if ((n = t.stopImmediatePropagation) == null || n.call(t), e._match = [...i].find((e) => mo(e) === o), r && (r.value = e._value), e._match) return Za(e, e._match, a);
		lo(e);
	}, no = (e, t) => {
		t.ctrlKey || t.metaKey || t.shiftKey || t.key === "Alt" || (e.control === t.target ? ro(e, t) : io(e, t));
	}, ro = (e, t) => {
		var n;
		let { _match: r, clear: i, control: a, creatable: o, items: s, list: c, multiple: l } = e;
		(t.key === "ArrowLeft" || t.key === "Backspace") && !a?.selectionEnd && ((n = s[s.length - 1]) == null || n.focus(), t.preventDefault()), t.key === "Enter" && a && (c || o) && (Fa(a), Za(e, l ? Xa(e) : r, l)), t.key === "Tab" && !t.shiftKey && i && !i.hidden && (t.preventDefault(), K(i, "aria-hidden", "false"), K(i, "tabindex", "0"), i.focus(), ba(i, "blur", () => lo(e), ha));
	}, io = (e, t) => {
		var n;
		let { clear: r, control: i, items: a } = e, { key: o, repeat: s, target: c } = t, l = [...a].indexOf(c);
		if ((o === " " || o === "Enter") && (a[l] || c === r)) return (n = a[l] || r) == null || n.click(), t.preventDefault();
		if (a[l]) {
			if (o.startsWith("Arrow") && t.preventDefault(), o === "ArrowLeft") return a[l - 1]?.focus();
			if (o === "ArrowRight") return (a[l + 1] || i)?.focus();
			if (o === "Backspace") return t.preventDefault(), s || Za(e, a[l]);
			i?.focus();
		}
	}, ao = (e, t) => {
		var n;
		if (!e.control) return;
		let { _texts: r, control: i, items: a, list: o, multiple: s, toggle: c } = e, l = [];
		for (let { addedNodes: e, removedNodes: n } of t || []) {
			for (let t of e) t.nodeName === "DATA" && l.unshift(t);
			for (let e of n) e.nodeName === "DATA" && l.push(e);
		}
		let u = Ta(e);
		if ((s ? l.length === 1 : l[0] === u) && e.contains(u)) {
			let t = i ? K(i, Y) : null;
			e._speak = `${r[l[0].isConnected ? "added" : "removed"]} ${q(l[0])}, `, K(i, Y, `${e._speak}${Ea(i)}`), e._focusMoved || setTimeout(() => za(e._speak.slice(0, -2))), setTimeout(oo, 300, e, t);
		}
		if (!s) {
			let t = q(a[0]);
			t !== e._itemSingleVale && fo(e), e._itemSingleVale = t;
		}
		so(e), uo(e), co(e), c && K(c, "aria-expanded", o ? `${!o.hidden}` : qa);
		let d = `${a.length ? r.found.replace("%d", `${a.length}`) : r.empty}`;
		K(i, "aria-description", s ? d : null), K(i, "list", Da(o)), K(e._listbox, Y, r.items), (n = e._umutate) == null || n.takeRecords();
	}, oo = (e, t) => {
		e._speak = "", e.control && K(e.control, Y, t), so(e);
	}, so = (e) => {
		let { _texts: t, _speak: n, items: r } = e, i = 0;
		for (let e of r) K(e, Y, `${n}${q(e)}, ${t.remove}${fa ? `, ${++i} ${t.of} ${r.length}` : ""}`), K(e, "role", "option"), K(e, "slot", "items"), K(e, "tabindex", "-1"), K(e, "value", mo(e));
	}, co = (e) => {
		var t;
		if (e._select?.isConnected || (e._select = e.querySelector("select")), !e._select) return;
		let { _select: n, items: r, multiple: i } = e, a, o = 0;
		K(n, "multiple", i ? "" : null);
		for (let e of r) {
			let t = n?.options[o++], r = q(e), i = mo(e);
			t ? Object.assign(t, {
				defaultSelected: !0,
				selected: !0,
				text: r,
				value: i
			}) : (a ||= document.createDocumentFragment(), a.appendChild(new Option(r, i, !0, !0)));
		}
		if (a) n.appendChild(a);
		else for (let e of [...n.options].slice(o)) e.remove();
		(t = e._umutate) == null || t.takeRecords();
	}, lo = (e) => {
		let { clear: t, control: n, toggle: r, list: i } = e, a = !n?.value || n?.disabled || n?.readOnly;
		t?.nodeName === "DEL" && K(t, "role", "button"), t && (K(t, Y) || K(t, Y, e._texts.clear), K(t, "aria-hidden", `${fa || ua}`), K(t, "hidden", a ? "" : null), K(t, "tabindex", "-1")), r && (K(r, Y) || K(r, Y, e._texts.toggle), K(r, "aria-hidden", `${fa || ua}`), K(r, "hidden", a && i ? null : ""), K(r, "tabindex", "-1"), K(r, "type", "button"));
	}, uo = (e) => {
		if (!e.list) return;
		let { _texts: t, list: n, multiple: r, options: i, values: a } = e;
		K(n, "data-sr-of", t.of), K(n, va, `${r}`);
		for (let e of i) ho(e, a.includes(mo(e)));
	}, fo = (e) => {
		if (!e.control || !e.list || e.multiple) return;
		let { control: t, items: n } = e, r = q(n[0]), i = r ? "insertText" : "deleteContentBackward";
		r !== t.value && ja(t, r, i);
	}, po = (e) => K(e, "label") ?? q(e), mo = (e) => K(e, "value") ?? q(e), ho = (e, t) => K(e, "selected", t ? "" : null), go = (e) => e.selected ?? e.hasAttribute("selected"), Aa.define("u-combobox", Ya);
})), vo, yo, bo, xo, So, Co = e((() => {
	O(), _o(), vo = "data-empty", yo = class extends Ya {
		_unmutate;
		connectedCallback() {
			super.connectedCallback(), this._unmutate = T(this, bo, { childList: !0 }), x(this, "comboboxafterselect input", xo, l), x(this, "toggle", So, l);
		}
		disconnectedCallback() {
			super.disconnectedCallback(), this._unmutate?.(), this._unmutate = void 0, S(this, "comboboxafterselect input", xo, l), S(this, "toggle", So, l);
		}
	}, bo = (e) => {
		let { control: t, list: n } = e, r = n || e.querySelector("u-datalist");
		t && m(t, "popovertarget", n ? a(n) : null), r && (m(r, "popover", "manual"), m(r, "data-is-floating", "true")), xo({ currentTarget: e });
	}, xo = ({ currentTarget: e }) => {
		let { creatable: t, control: n, options: r } = e;
		if (!r) return;
		let i = n?.value.trim() || "", a = i.toLowerCase(), o, s = !i;
		for (let e of r) if (!o && e.hasAttribute(vo) ? o = e : !s && e.label?.toLowerCase() === a && (s = !0), s && o) break;
		if (!o || (o.hidden = s, o.label = i, o.value = t ? i : "", !t || o.textContent)) return;
		let c = _(o, vo);
		c ? m(o, vo, c) : p(`Missing ${vo} value on:`, o), m(o, "data-create", c?.replace("{value}", i));
	}, So = (e) => {
		let t = e.currentTarget, n = e.newState === "open" && t.control;
		n && t.list?.dispatchEvent(new CustomEvent("ds-toggle-source", {
			bubbles: !0,
			composed: !0,
			detail: n
		}));
	}, E.define("ds-suggestion", yo);
})), wo, To, Eo, Do, Oo, ko, Ao, jo, X, Mo, No, Po, Fo, Io, Lo, Ro, zo, Bo, Vo, Ho, Uo, Wo, Go, Ko, qo, Jo, Yo, Xo, Zo, Qo, $o, es, ts, ns, rs, is, as, os, ss, Z, cs = e((() => {
	wo = () => typeof window < "u" && window.document !== void 0 && window.navigator !== void 0, To = wo(), Eo = To ? navigator.userAgent : "", Do = /android/i.test(Eo), To && /^Mac/i.test(navigator.userAgentData?.platform || navigator.platform), Oo = To && window.CSSStyleSheet && document.adoptedStyleSheets, ko = ":host(:not([hidden])) { display: block }", Ao = `${Do ? "data" : "aria"}-labelledby`, jo = typeof HTMLElement > "u" ? class {} : HTMLElement, X = (e, t, n) => n === void 0 ? e.getAttribute(t) : (n === null ? e.removeAttribute(t) : e.getAttribute(t) !== n && e.setAttribute(t, n), null), Mo = (e, ...t) => {
		let [n, ...r] = t;
		for (let t of n.split(" ")) e.addEventListener(t, ...r);
		return () => No(e, ...t);
	}, No = (e, ...t) => {
		let [n, ...r] = t;
		for (let t of n.split(" ")) e.removeEventListener(t, ...r);
	}, Po = (e, t) => {
		let n = e.shadowRoot || e.attachShadow({ mode: "open" });
		if (n.querySelector("slot") || n.append(Ro("slot")), !n.querySelector("style")) {
			if (!Oo) n.append(Ro("style", null, t));
			else {
				let e = new CSSStyleSheet();
				e.replaceSync(t), n.adoptedStyleSheets = [e];
			}
		}
		return n;
	}, Fo = (e, t, n) => {
		let r = new MutationObserver((n) => {
			if (!wo() || !e.isConnected) return i();
			t(e, n);
		}), i = Object.assign(() => r.disconnect(), { takeRecords: () => r.takeRecords() });
		return t(e), r.observe(e, n), i;
	}, Io = (e) => {
		let t = e.getRootNode?.call(e) || e.ownerDocument;
		return t instanceof Document || t instanceof ShadowRoot ? t : document;
	}, Lo = (e) => {
		if (!e || !To) return null;
		if (window.uElementsId || (window.uElementsId = {}), !e.id) {
			let t = e.nodeName.toLowerCase();
			window.uElementsId[t] || (window.uElementsId[t] = 1), e.id = `:${t}${window.uElementsId[t]++}`;
		}
		return e.id;
	}, Ro = (e, t, n) => {
		let r = document.createElement(e);
		if (n && (r.textContent = n), t) for (let [e, n] of Object.entries(t)) X(r, e, n);
		return r;
	}, zo = { define: (e, t) => !wo() || window.customElements.get(e) || window.customElements.define(e, t) }, Bo = (e, t = "<slot></slot>") => `<template shadowrootmode="open">${t}<style>${e}</style></template>`, Vo = "aria-controls", Ho = "aria-disabled", Uo = "aria-selected", Wo = "tabindex", Go = ko, Ko = ko, qo = `${ko}
::slotted([role="tab"]:not([hidden])) { display: inline-block; cursor: pointer }
::slotted([role="tab"][${Ho}="true"]) { cursor: default }`, Bo(Go), Bo(qo), Bo(Ko), Jo = class extends jo {
		constructor() {
			super(), Po(this, Go);
		}
		get tabList() {
			return this.querySelector("[role=\"tablist\"]:not(:scope [role=\"tabpanel\"] [role=\"tablist\"])");
		}
		get selectedIndex() {
			let e = as(this.tabList);
			return e.indexOf(is(e));
		}
		set selectedIndex(e) {
			Z(this.tabs[e]);
		}
		get tabs() {
			return this.querySelectorAll("[role=\"tab\"]:not(:scope [role=\"tabpanel\"] [role=\"tab\"])");
		}
		get panels() {
			return this.querySelectorAll("[role=\"tabpanel\"]:not(:scope [role=\"tabpanel\"] [role=\"tabpanel\"])");
		}
	}, Yo = class extends jo {
		constructor() {
			super(), Po(this, qo);
		}
		connectedCallback() {
			X(this, "role", "tablist"), Mo(this, "click keydown", this), this._umutate = Fo(this, Qo, {
				attributeFilter: [
					"id",
					"role",
					Vo,
					Uo
				],
				attributes: !0,
				childList: !0,
				subtree: !0
			});
		}
		disconnectedCallback() {
			var e;
			No(this, "click keydown", this), (e = this._umutate) == null || e.call(this), this._umutate = void 0;
		}
		handleEvent(e) {
			var t, n, r, i;
			let { key: a, type: o, target: s } = e, c = as(this), l = c.findIndex((e) => e.contains(s)), u = a === " " || a === "Enter", d = l;
			if (!(e.defaultPrevented || l === -1) && (o === "click" && Z(c[l]), o === "keydown")) {
				if (u) return (t = e.preventDefault) == null || t.call(e), c[l].dispatchEvent(new MouseEvent("click", {
					bubbles: !0,
					cancelable: !0,
					composed: !0
				}));
				if (a === "ArrowDown" || a === "ArrowRight") d = (l + 1) % c.length;
				else if (a === "ArrowUp" || a === "ArrowLeft") d = (l || c.length) - 1;
				else if (a === "End") d = c.length - 1;
				else if (a === "Home") d = 0;
				else if (a === "Tab" && !rs(c[l])) {
					let e = is(c);
					return e && X(e, Wo, "-1"), e && setTimeout(() => X(e, Wo, "0"));
				} else return;
				(n = e.preventDefault) == null || n.call(e), (i = (r = c[d]).focus) == null || i.call(r);
			}
		}
		get tabsElement() {
			return ss(this);
		}
		get tabs() {
			return this.querySelectorAll(":scope > [role=\"tab\"]");
		}
		get selectedIndex() {
			let e = as(this);
			return e.indexOf(is(e));
		}
		set selectedIndex(e) {
			Z(this.tabs[e]);
		}
	}, Xo = class extends jo {
		static get observedAttributes() {
			return [
				"id",
				Uo,
				Vo
			];
		}
		connectedCallback() {
			X(this, "role", "tab"), X(this, Uo, `${this.selected}`), X(this, Wo, this.selected ? "0" : "-1"), is(this.parentElement?.children) || Z(this);
		}
		attributeChangedCallback() {
			!es.has(this.parentElement) && this.selected && Z(this);
		}
		get tabsElement() {
			return ss(this);
		}
		get tabList() {
			let e = this.parentElement;
			return e instanceof Yo ? e : null;
		}
		get selected() {
			return rs(this);
		}
		set selected(e) {
			e && Z(this);
		}
		get index() {
			return Math.max(as(this.parentElement).indexOf(this), 0);
		}
		get panel() {
			let e = ss(this)?.panels, t = as(this.parentElement).indexOf(this);
			return os(this, e?.[t]);
		}
	}, Zo = class extends jo {
		constructor() {
			super(), Po(this, Ko);
		}
		connectedCallback() {
			X(this, "role", "tabpanel");
			let e = new Set(this.tabs), t = ss(this), n = t?.tabs[Array.prototype.indexOf.call(t?.panels, this)];
			n && !n.hasAttribute(Vo) && e.add(n);
			let r = is(e) || e.values().next().value;
			ts(r, () => $o(this, r, !!r && rs(r)));
		}
		get tabsElement() {
			return ss(this);
		}
		get tabs() {
			return Io(this).querySelectorAll(`[${Vo}="${Lo(this)}"]`);
		}
	}, Qo = (e, t = []) => {
		let n;
		for (let { target: e } of t) e instanceof Element && X(e, "role") === "tab" && rs(e) && (n = e);
		Z(n);
	}, $o = (e, t, n = !1) => {
		X(e, "aria-hidden", `${!n}`), X(e, "hidden", n ? null : ""), X(e, Wo, n ? "0" : null), t && X(t, Vo, Lo(e)), t && n && rs(t) && X(e, Ao, Lo(t));
	}, es = /* @__PURE__ */ new Set(), ts = (e, t) => {
		var n;
		let r = e?.parentElement;
		r && es.add(r), t(), r && es.delete(r), (n = r?._umutate) == null || n.takeRecords();
	}, ns = (e) => X(e, Ho) !== "true", rs = (e) => X(e, Uo) === "true", is = (e = []) => {
		for (let t of e) if (rs(t)) return t;
	}, as = (e) => {
		let t = [];
		for (let n of e?.children || []) n.getAttribute("role") === "tab" && t.push(n);
		return t;
	}, os = (e, t = null) => {
		let n = X(e, Vo);
		return n ? Io(e).getElementById(n) : t;
	}, ss = (e) => {
		for (let t = e; t; t = t.parentNode || t.host) if (t instanceof Jo) return t;
		return null;
	}, Z = (e) => e && ns(e) && ts(e, () => {
		let t = as(e.parentElement), n = ss(e)?.panels || [], r = os(e, n?.[t.indexOf(e)]), i = 0;
		for (let a of t) {
			let t = os(a, n[i++]);
			X(a, Uo, `${a === e}`), X(a, Wo, a === e ? "0" : "-1"), t && $o(t, a, t === r);
		}
	}), zo.define("u-tabs", Jo), zo.define("u-tablist", Yo), zo.define("u-tab", Xo), zo.define("u-tabpanel", Zo);
})), ls, us, ds, fs, ps = e((() => {
	O(), cs(), ls = class extends Jo {}, us = class extends Yo {}, ds = class extends Xo {}, fs = class extends Zo {}, E.define("ds-tabs", ls), E.define("ds-tablist", us), E.define("ds-tab", ds), E.define("ds-tabpanel", fs);
})), ms, hs, gs, _s, vs, ys, bs, xs, Ss, Cs, ws, Ts, Es, Ds, Os, ks, As, js, Ms, Q, Ns, Ps, Fs, Is, Ls, Rs, zs, Bs, Vs, Hs, Us, Ws, Gs, Ks, qs, Js, $, Ys, Xs, Zs, Qs, $s, ec, tc, nc, rc, ic, ac, oc, sc, cc, lc, uc, dc, fc, pc, mc, hc, gc, _c, vc, yc, bc, xc, Sc, Cc = e((() => {
	ms = Object.defineProperty, hs = Object.getOwnPropertySymbols, gs = Object.prototype.hasOwnProperty, _s = Object.prototype.propertyIsEnumerable, vs = (e, t, n) => t in e ? ms(e, t, {
		enumerable: !0,
		configurable: !0,
		writable: !0,
		value: n
	}) : e[t] = n, ys = (e, t) => {
		for (var n in t ||= {}) gs.call(t, n) && vs(e, n, t[n]);
		if (hs) for (var n of hs(t)) _s.call(t, n) && vs(e, n, t[n]);
		return e;
	}, bs = () => typeof window < "u" && window.document !== void 0 && window.navigator !== void 0, xs = bs(), Ss = xs ? navigator.userAgent : "", Cs = /android/i.test(Ss), ws = /firefox/i.test(Ss), Ts = /iPad|iPhone|iPod/.test(Ss), Es = xs && /^Mac/i.test(navigator.userAgentData?.platform || navigator.platform), Ds = xs && window.CSSStyleSheet && document.adoptedStyleSheets, Os = {
		once: !0,
		capture: !0,
		passive: !0
	}, ks = ":host(:not([hidden])) { display: block }", As = "outline: 1px dotted; outline: 5px auto Highlight; outline: 5px auto -webkit-focus-ring-color", js = `${Cs ? "data" : "aria"}-multiselectable`, Ms = typeof HTMLElement > "u" ? class {} : HTMLElement, Q = (e, t, n) => n === void 0 ? e.getAttribute(t) : (n === null ? e.removeAttribute(t) : e.getAttribute(t) !== n && e.setAttribute(t, n), null), Ns = (e, ...t) => {
		let [n, ...r] = t;
		for (let t of n.split(" ")) e.addEventListener(t, ...r);
		return () => Ps(e, ...t);
	}, Ps = (e, ...t) => {
		let [n, ...r] = t;
		for (let t of n.split(" ")) e.removeEventListener(t, ...r);
	}, Fs = (e, t) => {
		let n = e.shadowRoot || e.attachShadow({ mode: "open" });
		if (n.querySelector("slot") || n.append(Bs("slot")), !n.querySelector("style")) {
			if (!Ds) n.append(Bs("style", null, t));
			else {
				let e = new CSSStyleSheet();
				e.replaceSync(t), n.adoptedStyleSheets = [e];
			}
		}
		return n;
	}, Is = (e, t, n) => {
		let r = new MutationObserver((n) => {
			if (!bs() || !e.isConnected) return i();
			t(e, n);
		}), i = Object.assign(() => r.disconnect(), { takeRecords: () => r.takeRecords() });
		return t(e), r.observe(e, n), i;
	}, Ls = (e) => {
		let t = e.getRootNode?.call(e) || e.ownerDocument;
		return t instanceof Document || t instanceof ShadowRoot ? t : document;
	}, Rs = (e) => Ls(e).activeElement, zs = (e) => {
		if (!e || !xs) return null;
		if (window.uElementsId || (window.uElementsId = {}), !e.id) {
			let t = e.nodeName.toLowerCase();
			window.uElementsId[t] || (window.uElementsId[t] = 1), e.id = `:${t}${window.uElementsId[t]++}`;
		}
		return e.id;
	}, Bs = (e, t, n) => {
		let r = document.createElement(e);
		if (n && (r.textContent = n), t) for (let [e, n] of Object.entries(t)) Q(r, e, n);
		return r;
	}, Vs = { define: (e, t) => !bs() || window.customElements.get(e) || window.customElements.define(e, t) }, Hs = (e, t, n = "") => {
		var r;
		let i = {
			bubbles: !0,
			composed: !0,
			data: t,
			inputType: n
		}, a = HTMLInputElement.prototype;
		e.dispatchEvent(new InputEvent("beforeinput", i)), (r = Object.getOwnPropertyDescriptor(a, "value")?.set) == null || r.call(e, t), e.dispatchEvent(new InputEvent("input", i)), e.dispatchEvent(new Event("change", { bubbles: !0 }));
	}, Us = /* @__PURE__ */ new WeakSet(), Ws = (e, t) => (t?.type === "pointerdown" && (Us.add(e), Ns(document, "pointerup", () => Us.delete(e), Os)), Us.has(e)), Gs = (e, t = "<slot></slot>") => `<template shadowrootmode="open">${t}<style>${e}</style></template>`, Ks = (e) => {
		let t = Q(e, "form");
		Q(e, "form", "#"), setTimeout(qs, 0, e, t);
	}, qs = (e, t) => Q(e, "form", t), Js = (e) => (e?.textContent)?.trim() || "", Ys = 0, Xs = 0, Zs = (e) => {
		clearTimeout(Xs), $ || ($ = Bs("div", { "aria-live": "assertive" }), $.style.overflow = "hidden", $.style.position = "fixed", $.style.whiteSpace = "nowrap", $.style.width = "1px"), $.isConnected || document.body.append($), e === "" && ($.textContent = ""), e && ($.textContent = `${e}${Ys++ % 2 ? "\xA0" : ""}`, Xs = setTimeout(Zs, !Es && ws ? 2e3 : 300, ""));
	}, Qs = "disabled", $s = "selected", ec = class extends Ms {
		constructor() {
			super(...arguments), this._skipAttrChange = !1;
		}
		static get observedAttributes() {
			return [
				"id",
				Qs,
				$s
			];
		}
		connectedCallback() {
			Cs && (this.tabIndex = -1), this.hasAttribute("role") || Q(this, "role", "option"), Q(this, "aria-disabled", `${this.disabled}`), Q(this, "aria-selected", `${this.selected}`), zs(this);
		}
		attributeChangedCallback(e) {
			this._skipAttrChange ||= (this._skipAttrChange = !0, e === "id" ? zs(this) : Q(this, `aria-${e}`, `${this[e]}`), !1);
		}
		get defaultSelected() {
			return this[$s];
		}
		set defaultSelected(e) {
			this[$s] = e;
		}
		get disabled() {
			return Q(this, Qs) !== null;
		}
		set disabled(e) {
			Q(this, Qs, e ? "" : null);
		}
		get form() {
			return this.closest("form");
		}
		get index() {
			return [...this.parentElement?.options || [this]].indexOf(this);
		}
		get label() {
			return Q(this, "label") ?? this.text;
		}
		set label(e) {
			Q(this, "label", e);
		}
		get selected() {
			return Q(this, $s) !== null;
		}
		set selected(e) {
			Q(this, $s, e ? "" : null);
		}
		get text() {
			return Js(this);
		}
		set text(e) {
			this.textContent = e;
		}
		get value() {
			return Q(this, "value") ?? this.text;
		}
		set value(e) {
			Q(this, "value", e);
		}
	}, Vs.define("u-option", ec), tc = "data-activedescendant", nc = "aria-hidden", rc = `${ks}
::slotted([role="option"]) { display: block; cursor: pointer }
::slotted([role="option"][${tc}]) { ${As} }
::slotted([role="option"]:is([${nc}="true"], [disabled], [hidden])) { display: none !important }`, Gs(rc), ac = "focus click keydown", oc = "focusout input pointerdown", sc = {
		of: "of",
		plural: "%d hits",
		singular: "%d hit"
	}, cc = class extends Ms {
		constructor() {
			super(), this._texts = ys({}, sc), Fs(this, rc);
		}
		static get observedAttributes() {
			return ["id", ...Object.keys(sc).map((e) => `data-sr-${e}`)];
		}
		connectedCallback() {
			this._root = Ls(this), Q(this, "hidden", ""), Q(this, "role", "listbox"), Q(this, "tabindex", "-1"), Ns(this._root, ac, this, !0), this._umutate = Is(this, xc, {
				attributeFilter: [
					"disabled",
					"hidden",
					"label",
					"value"
				],
				attributes: !0,
				characterData: !0,
				childList: !0,
				subtree: !0
			});
		}
		disconnectedCallback() {
			var e;
			this._root && Ps(this._root, ac, this, !0), this._root && Ps(this._root, oc, this), mc(this, null), (e = this._umutate) == null || e.call(this), this._umutate = this._root = this._input = void 0;
		}
		attributeChangedCallback(e, t, n) {
			let r = e?.split("data-sr-")[1];
			if (sc[r]) this._texts[r] = n || sc[r];
			else if (this.id) {
				let e = `input[list="${this.id}"]`, t = Ls(this).querySelector(e);
				t && mc(this, t);
			}
		}
		handleEvent(e) {
			e.defaultPrevented || (e.type === "focus" && hc(this, e), e.type === "focusout" && gc(this, e), e.type === "click" && yc(this, e), e.type === "input" && xc(this), e.type === "keydown" && bc(this, e), e.type === "pointerdown" && vc(this, e));
		}
		get options() {
			let e = !this._options && this.querySelector("[role=\"option\"],option");
			return e && (this._options = this.getElementsByTagName(e?.nodeName)), this._options || this.getElementsByTagName("option");
		}
	}, lc = (e) => e.disabled || e.readOnly, uc = (e) => e instanceof HTMLInputElement, dc = (e) => Q(e, nc) !== "true" && e.clientHeight, fc = (e, t) => {
		e._input && Q(e._input, "aria-expanded", `${t}`), !(!e.isConnected || e.hidden !== t) && (t || pc(e), e.popover && e.togglePopover(t), t && setTimeout(() => clearTimeout(ic)), e.hidden = !t);
	}, pc = (e, t) => {
		e._input && Q(e._input, "aria-activedescendant", zs(t));
		for (let n of e.options) Q(n, tc, n === t ? "" : null);
		t?.scrollIntoView({ block: "nearest" });
	}, mc = (e, t) => {
		let n = t || e._input, r = !e._root || e.hidden;
		n && (e.popover && (Q(e, "popover", "manual"), Q(n, "popovertarget", t && zs(e))), Q(n, "aria-autocomplete", t && "list"), Q(n, "aria-controls", t && zs(e)), Q(n, "aria-expanded", t && `${!r}`), Q(n, "autocomplete", t && "off"), Q(n, "enterkeyhint", "done"), Q(n, "role", t && !lc(n) ? "combobox" : null));
	}, hc = (e, { target: t }) => {
		if (e._input !== t) {
			if (uc(t) && t.getAttribute("list") === e.id) {
				if (mc(e, null), mc(e, t), lc(t)) return;
				e._root && Ns(e._root, oc, e), Q(e, "aria-label", (t.labels?.[0])?.textContent.trim() || null), Zs(), e._input = t, Cs && fc(e, !0);
			} else _c(e);
		}
	}, gc = (e, t) => {
		Ws(e) ? t.stopImmediatePropagation() : Cs || setTimeout(_c, 0, e, t);
	}, _c = (e) => {
		let t = Rs(e);
		e._input === t || e.contains(t) || (e._root && Ps(e._root, oc, e), fc(e, !1), e._input = void 0);
	}, vc = (e, t) => e.contains(t.target) && Ws(e, t), yc = (e, t) => {
		var n;
		if (uc(t.target) && hc(e, t), e._input === t.target) return fc(e, !0);
		if (!e.contains(t.target)) return _c(e);
		for (let r of e.options) if (r.contains(t.target)) return (n = e._input) == null || n.focus(), e._input && Hs(e._input, r.value), fc(e, Q(e, js) === "true");
	}, bc = (e, t) => {
		if (uc(t.target) && hc(e, t), e._input !== t.target) return;
		let { key: n, ctrlKey: r, metaKey: i, altKey: a } = t;
		if (r || i || n === "Shift" || n === "Tab" || a && !n.startsWith("Arrow")) return;
		n === "Escape" && !e.hidden && t.preventDefault(), fc(e, n !== "Escape");
		let o = n === "Enter", s = Q(e._input, "aria-activedescendant"), c = [], l = -1, u = -1;
		for (let t of e.options) dc(t) && (c.push(t), t.id === s && (l = c.length - 1));
		!a && n === "ArrowDown" && (u = (l + 1) % c.length), !a && n === "ArrowUp" && (u = (l || c.length) - 1), ~l && ((n === "Home" || n === "PageUp") && (u = 0), (n === "End" || n === "PageDown") && (u = c.length - 1), o && (u = l)), c[u] && (t.preventDefault(), e._input.focus()), o && c[u] ? (t.stopImmediatePropagation(), Ks(e._input), c[u].click()) : pc(e, c[u]);
	}, xc = (e) => {
		var t;
		if (!e._input || e.hidden) return;
		let n = e._input.value.toLowerCase().trim() || "", r = !e.hasAttribute("data-nofilter"), i = [], a = [];
		for (let t of e.options) (t.disabled || t.hidden || r && !t.label.toLowerCase().includes(n) ? i : a).push(t);
		for (let e of i) Q(e, nc, "true");
		for (let e of a) Q(e, nc, "false");
		clearTimeout(ic), ic = setTimeout(Sc, 500, e, a), Ts && a.forEach((t, n, { length: r }) => {
			Q(t, "title", `${n + 1} ${e._texts.of} ${r}`);
		}), (t = e._umutate) == null || t.takeRecords();
	}, Sc = (e, t) => !e.hidden && e._input === Rs(e) && Zs(`${t.some(({ value: e }) => e) ? `${e._texts[t[1] ? "plural" : "singular"]}`.replace("%d", `${t.length}`) : e.innerText}`), bs() && Object.defineProperty(HTMLInputElement.prototype, "list", {
		configurable: !0,
		enumerable: !0,
		get() {
			let e = Q(this, "list");
			return e && Ls(this).getElementById(e);
		}
	}), Vs.define("u-datalist", cc);
})), wc = e((() => {
	me(), we(), je(), Pe(), tt(), at(), Gr(), Xr(), si(), Ei(), ki(), Mi(), Ki(), ea(), Co(), ps(), Cc();
})), Tc = e((() => {})), Ec = e((() => {})), Dc = /* @__PURE__ */ t((() => {
	wc(), Tc(), Ec();
}));
//#endregion
export default Dc();
