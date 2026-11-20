// 图标路径沿用已确认的可点击稿，仅使用固定路径，不接收账户内容。
namespace Ui {
  const paths={
    shield:'M12 3 20 6v6c0 5-8 9-8 9S4 17 4 12V6ZM8 12l3 3 5-6',
    search:'M16.5 10a6.5 6.5 0 1 1-13 0 6.5 6.5 0 0 1 13 0ZM15 15l5 5',
    plus:'M12 5v14M5 12h14',
    check:'m5 12 4 4L19 6',
    folder:'M2 8V6a2 2 0 0 1 2-2h5l3 3h8a2 2 0 0 1 2 2H2Zm0 3h20v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2Z',
    key:'M22 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0ZM14 12H3v3h3v-3m3 0v3',
    grip:'M8 5h.01M16 5h.01M8 12h.01M16 12h.01M8 19h.01M16 19h.01',
    gear:'m9 3-.6 2.5-2 .9L4 5.7 2 9l1.8 1.8v2.4L2 15l2 3.3 2.4-.7 2 .9L9 21h6l.6-2.5 2-.9 2.4.7 2-3.3-1.8-1.8v-2.4L22 9l-2-3.3-2.4.7-2-.9L15 3ZM15.2 12a3.2 3.2 0 1 1-6.4 0 3.2 3.2 0 0 1 6.4 0Z',
    copy:'M10 8h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-8a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2ZM16 8V4a1 1 0 0 0-1-1H4a1 1 0 0 0-1 1v11a1 1 0 0 0 1 1h4',
    more:'M5 12h.01M12 12h.01M19 12h.01',
    chevron:'m9 5 7 7-7 7',down:'m6 9 6 6 6-6',back:'m14 5-7 7 7 7M7 12h13',
    lock:'M7 10h10a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2ZM8 10V7a4 4 0 0 1 8 0v3M12 14v3',
    sun:'M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0ZM12 2v2m0 16v2M2 12h2m16 0h2M5 5l1 1m12 12 1 1M5 19l1-1M18 6l1-1',
    scan:'M3 8V3h5m8 0h5v5m0 8v5h-5M8 21H3v-5M3 12h18M7 7h3v3H7ZM15 7h2v3M7 16v1h3m5-3v3h2',
    image:'M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2ZM9 9H7M4 17l5-5 4 4 3-3 5 5',
    export:'M12 16V3m-4 4 4-4 4 4M4 13v7h16v-7',
    import:'M12 3v13m-4-4 4 4 4-4M4 16v5h16v-5',
    info:'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM12 11v6m0-10v.1'
  };
  export function icon(name:keyof typeof paths):SVGSVGElement {
    const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');
    svg.setAttribute('viewBox','0 0 24 24');svg.setAttribute('aria-hidden','true');svg.classList.add('icon');
    const path=document.createElementNS(svg.namespaceURI,'path');path.setAttribute('d',paths[name]);svg.append(path);return svg;
  }
  document.querySelectorAll<HTMLElement>('[data-icon]').forEach(host=>host.replaceChildren(icon(host.dataset.icon as keyof typeof paths)));
  export function show(page:'home'|'editor'|'settings'):void {
    document.getElementById('homePage')!.hidden=page!=='home';
    document.getElementById('editor')!.hidden=page!=='editor';
    document.getElementById('settingsDialog')!.hidden=page!=='settings';
    document.body.dataset.page=page;
  }
}
