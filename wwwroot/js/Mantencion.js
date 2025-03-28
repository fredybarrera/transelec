$(function () {
    $("#btn-rechazar-om").on("click", function () {

        const objectid = $(this).data("objectid");
        $("#Objectid").val(objectid);

        $("#modal-rechazar").modal('toggle');
    });
});


async function aprobarOm(Objectid) {

    
    let payload = { Objectid: Objectid };

    console.log("payload::: ", payload);

    let response = await fetch("/Home/AprobarOm", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    });

    console.log("response:::", response);

    if (response.ok) {
        let data = await response.json();
        toastr.success(data.mensaje);
        setTimeout(function () {
            location.reload();
        }, 1000)
    } else {
        const errorData = await response.json();
        console.log("errorData:::", errorData);
        toastr.error(errorData.mensaje);
    }
};


async function rechazarOm() {

    const Objectid = $("#Objectid").val();
    const Observacion = $("#Observacion").val();

    if (Observacion == "") {
        toastr.error("Debe ingresar la observación");
        return;
    }

    let payload = { Objectid, Observacion };

    console.log("payload::: ", payload);

    let response = await fetch("/Home/RechazarOm", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    });

    console.log("response:::", response);

    if (response.ok) {
        let data = await response.json();
        toastr.success(data.mensaje);
        setTimeout(function () {
            location.reload();
        }, 1000)
    } else {
        const errorData = await response.json();
        console.log("errorData:::", errorData);
        toastr.error(errorData.mensaje);
    }
};

async function aceptarActividad(Objectid, Key) {


    let payload = { Objectid, Key };

    console.log("payload::: ", payload);

    let response = await fetch("/Home/AceptarActividad", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    });

    console.log("response:::", response);

    if (response.ok) {
        let data = await response.json();
        toastr.success(data.mensaje);
        setTimeout(function () {
            location.reload();
        }, 1000)
    } else {
        const errorData = await response.json();
        console.log("errorData:::", errorData);
        toastr.error(errorData.mensaje);
    }
};





