import fitz, json
NEW_PDF="../../FUR/Formulario-automotor.pdf"
man=json.load(open("artifacts/fur-analysis/calib/manifest-792.json",encoding="utf-8"))

# sample AUTOMOTOR matricula values (mirrors AutomotorData + FurFieldMapper)
V={
 "traffic_secretary_name":"STRIATTOYTTE MCPAL FUNDA","traffic_secretary_city":"FUNDA","traffic_secretary_code":"25286000",
 "processing_day":"25","processing_month":"6","processing_year":"2026","plate_letter":"YYY","plate_number":"090",
 "requested_process_1":"X","vehicle_class_5":"X",
 "vehicle_brand":"TESLA","vehicle_line":"MODELO Y","vehicle_colors":"BLANCO PERLA","vehicle_model":"2026",
 "vehicle_displacement":"0","vehicle_capacity":"5","vehicle_fuel_type_1":"X",
 "is_armored_vehicle_no":"X","is_dismantling_armor_no":"X",
 "vehicle_bodywork_type":"SUV","vehicle_engine_number":"TM-495717","vehicle_chassis_number":"LRWYGCFJ7TC495717",
 "vehicle_service_type_1":"X","vehicle_vin_number":"LRWYGCFJ7TC495717",
 "vehicle_owner_first_last_name":"AMADO","vehicle_owner_second_last_name":"GARCIA","vehicle_owner_name":"DANIEL",
 "vehicle_owner_document_type_c":"X","vehicle_owner_document_number":"1193552679",
 "vehicle_owner_address":"CALLE 1 # 2-3","vehicle_owner_city":"FUNZA","vehicle_owner_phone":"3001234567",
 "vehicle_owner_signature":"Validacion biometrica CC 1193552679\nUUID abc-123\nAprob 25/06/2026 Vence 25/07/2026",
 "observations":"",
}
doc=fitz.open(NEW_PDF); pg=doc[0]
red=(0.8,0,0)
def draw(f,txt):
    fs=f.get("fontSize",7); x=f["x"]; y=f["y"]
    if f["type"]=="checkbox":
        fs=(f.get("size",9))+2
        pg.insert_text((x,y+f.get("size",9)*0.85),"X",fontsize=fs,color=red,fontname="hebo")
        return
    lines=txt.split("\n") if f["type"]=="multiline" else [txt]
    lh=fs*1.25
    for i,ln in enumerate(lines):
        if not ln.strip(): continue
        dx=x
        if f.get("w",0)>0 and f.get("align")!="left":
            tw=fitz.get_text_length(ln,fontname="hebo",fontsize=fs)
            if f.get("align")=="center": dx=x+(f["w"]-tw)/2
            elif f.get("align")=="right": dx=x+f["w"]-tw
        yb=y+i*lh+fs*0.82
        fn="hebo" if f.get("bold") else "helv"
        pg.insert_text((dx,yb),ln,fontsize=fs,color=red,fontname=fn)
for f in man["fields"]:
    if f["page"]!=1: continue
    v=V.get(f["id"])
    if v is None or v=="": continue
    draw(f,v)
pix=pg.get_pixmap(matrix=fitz.Matrix(2,2))
out="artifacts/fur-analysis/calib/preview-new-automotor-p1.png"
pix.save(out); print("wrote",out)
