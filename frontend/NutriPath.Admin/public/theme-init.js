// Applies the saved theme before first paint (a separate file so a strict CSP can forbid inline scripts).
try {
  var t = localStorage.getItem('nutripath-admin-theme') || 'system';
  var dark = t === 'dark' || (t === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  if (dark) document.documentElement.classList.add('dark');
} catch (e) {}
