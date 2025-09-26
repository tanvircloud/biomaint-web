// wwwroot/js/pricing.js
export function getLocaleInfo(){
  try{
    const tz   = Intl.DateTimeFormat().resolvedOptions().timeZone || "";
    const lang = (navigator.language || navigator.userLanguage || "").toLowerCase();
    return { tz, lang };
  }catch{ return { tz:"", lang:"" }; }
}

export function getSavedCurrency(){
  try{ return localStorage.getItem("bm_currency"); }catch{ return null; }
}

export function setSavedCurrency(val){
  try{ localStorage.setItem("bm_currency", val); }catch{}
}
